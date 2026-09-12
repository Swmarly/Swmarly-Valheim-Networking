#!/usr/bin/env python3
"""Strict static compatibility check for the Valheim assembly used by CI.

This is deliberately narrower than a version bypass. It proves that the exact methods, fields,
signatures, and literal counts relied on by the Harmony modules are still present. The mod's own
runtime preflight repeats the same checks before installing replacements.
"""
import argparse
import re
import subprocess
import sys


def run_type(ilspy, assembly, type_name, il=False):
    cmd = [ilspy]
    if il:
        cmd.append("--ilcode")
    cmd.extend(["--type", type_name, assembly])
    try:
        result = subprocess.run(cmd, check=False, stdout=subprocess.PIPE,
                                stderr=subprocess.STDOUT, text=True)
    except OSError as exc:
        raise RuntimeError("could not execute ilspycmd: %s" % exc)
    if result.returncode != 0:
        raise RuntimeError("ilspycmd failed for %s:\n%s" % (type_name, result.stdout))
    return result.stdout


def require(text, pattern, label, failures):
    if not re.search(pattern, text, re.MULTILINE | re.DOTALL):
        failures.append("%s not found" % label)


def require_field(text, field, failures):
    require(text, r"\b" + re.escape(field) + r"\b", "field " + field, failures)


def method_block(il_text, name):
    pattern = re.compile(r"\.method\b[^\{]*?\b" + re.escape(name) + r"\s*\(",
                         re.MULTILINE | re.DOTALL)
    match = pattern.search(il_text)
    if not match:
        return None
    opening = il_text.find("{", match.end())
    if opening < 0:
        return None
    depth = 0
    for index in range(opening, len(il_text)):
        if il_text[index] == "{":
            depth += 1
        elif il_text[index] == "}":
            depth -= 1
            if depth == 0:
                return il_text[opening:index + 1]
    return None


def count_ints(block, wanted):
    values = [int(value) for value in re.findall(
        r"\bldc\.i4(?:\.s)?\s+(-?\d+)\b", block)]
    return values.count(wanted)


def count_float(block, wanted):
    values = []
    for value in re.findall(r"\bldc\.r4\s+([^\s]+)", block):
        try:
            values.append(float(value.rstrip("f")))
        except ValueError:
            pass
    return sum(1 for value in values if abs(value - wanted) < 0.0001)


def require_il(il_text, method, kind, wanted, expected, failures):
    block = method_block(il_text, method)
    if block is None:
        failures.append("IL body for %s not found" % method)
        return
    actual = count_ints(block, wanted) if kind == "int" else count_float(block, wanted)
    if actual != expected:
        failures.append("%s expected %dx %s, found %d" %
                        (method, expected, ("ldc.i4 " if kind == "int" else "ldc.r4 ") +
                         str(wanted), actual))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--assembly", required=True)
    parser.add_argument("--ilspy", required=True)
    parser.add_argument("--expected-game-version", required=True)
    parser.add_argument("--expected-network-version", required=True, type=int)
    args = parser.parse_args()

    failures = []
    types = {}
    for name in ["Version", "ZDOMan", "ZSteamSocket", "ZSyncTransform", "ZRpc",
                 "ZNet", "ZNetScene", "Game", "WearNTear", "Heightmap", "ZDO"]:
        types[name] = run_type(args.ilspy, args.assembly, name)

    version_text = types["Version"]
    if args.expected_game_version not in re.findall(r"(?<!\d)\d+\.\d+\.\d+(?!\d)", version_text):
        failures.append("Version type does not contain expected game version %s" %
                        args.expected_game_version)
    network_matches = re.findall(
        r"(?:m_networkVersion|NetworkVersion|networkVersion)\s*=\s*(\d+)", version_text)
    if str(args.expected_network_version) not in network_matches:
        failures.append("Version type does not expose expected network version %d (found %s)" %
                        (args.expected_network_version, ",".join(network_matches) or "none"))

    zm = types["ZDOMan"]
    require(zm, r"\bSendZDOs\s*\(\s*(?:ZDOMan\.)?ZDOPeer\s+\w+\s*,\s*bool\s+\w+",
            "ZDOMan.SendZDOs(ZDOPeer,bool)", failures)
    require(zm, r"\bSendZDOToPeers2\s*\(\s*float\s+\w+", "ZDOMan.SendZDOToPeers2(float)", failures)
    require(zm, r"\bReleaseZDOS\s*\(\s*float\s+\w+", "ZDOMan.ReleaseZDOS(float)", failures)
    require(zm, r"\bCreateSyncList\s*\(", "ZDOMan.CreateSyncList", failures)
    require(zm, r"\bServerSortSendZDOS\s*\(", "ZDOMan.ServerSortSendZDOS", failures)
    for field in ["m_objectsByID", "m_peers", "m_nextSendPeer", "m_sendTimer",
                  "m_zdosSentLastSec", "m_zdosRecvLastSec"]:
        require_field(zm, field, failures)

    socket = types["ZSteamSocket"]
    require(socket, r"\bSendQueuedPackages\s*\(\s*\)", "ZSteamSocket.SendQueuedPackages()", failures)
    require(socket, r"\bRegisterGlobalCallbacks\s*\(\s*\)", "ZSteamSocket.RegisterGlobalCallbacks()", failures)
    for field in ["m_sendQueue", "m_totalSent", "m_con"]:
        require_field(socket, field, failures)

    sync = types["ZSyncTransform"]
    require(sync, r"\bSyncPosition\s*\(\s*ZDO\s+\w+\s*,\s*float\s+\w+\s*,\s*out\s+bool\s+\w+",
            "ZSyncTransform.SyncPosition(ZDO,float,out bool)", failures)

    rpc = types["ZRpc"]
    require(rpc, r"\bUpdate\s*\(\s*float\s+\w+\s*\)", "ZRpc.Update(float)", failures)

    require(types["ZNetScene"], r"\bCreateObjects\s*\(", "ZNetScene.CreateObjects", failures)
    require(types["ZNet"], r"\bSaveWorld\s*\(\s*bool\s+\w+", "ZNet.SaveWorld(bool)", failures)
    require(types["ZNet"], r"\bSaveWorldThread\s*\(", "ZNet.SaveWorldThread", failures)
    require(types["Game"], r"\bCollectResources\s*\(\s*bool\s+\w+", "Game.CollectResources(bool)", failures)
    require(types["WearNTear"], r"\bUpdateSupport\s*\(", "WearNTear.UpdateSupport", failures)
    require(types["WearNTear"], r"\bClearCachedSupport\s*\(", "WearNTear.ClearCachedSupport", failures)
    require(types["WearNTear"], r"\bOnDestroy\s*\(", "WearNTear.OnDestroy", failures)
    require(types["Heightmap"], r"\bRebuildRenderMesh\s*\(", "Heightmap.RebuildRenderMesh", failures)
    require(types["ZDO"], r"\bDeserialize\s*\(", "ZDO.Deserialize", failures)
    require_field(types["ZNetScene"], "m_instances", failures)
    for field in ["m_peer", "m_zdos", "m_forceSend", "m_invalidSector"]:
        peer_text = run_type(args.ilspy, args.assembly, "ZDOMan.ZDOPeer")
        require_field(peer_text, field, failures)
    peer_text = run_type(args.ilspy, args.assembly, "ZNetPeer")
    for field in ["m_socket", "m_uid", "m_playerName", "m_refPos", "m_simulationDistance"]:
        require_field(peer_text, field, failures)

    # IL counts are the assertions used by the actual transpilers.
    il_zm = run_type(args.ilspy, args.assembly, "ZDOMan", il=True)
    il_scene = run_type(args.ilspy, args.assembly, "ZNetScene", il=True)
    il_sync = run_type(args.ilspy, args.assembly, "ZSyncTransform", il=True)
    require_il(il_zm, "SendZDOs", "int", 10240, 2, failures)
    require_il(il_zm, "SendZDOs", "int", 2048, 1, failures)
    require_il(il_scene, "CreateObjects", "int", 10, 1, failures)
    require_il(il_zm, "ReleaseZDOS", "float", 2.0, 1, failures)
    require_il(il_sync, "SyncPosition", "float", 0.2, 2, failures)
    require_il(il_sync, "SyncPosition", "float", 2.0, 2, failures)

    if failures:
        print("Valheim compatibility validation FAILED:", file=sys.stderr)
        for failure in failures:
            print(" - " + failure, file=sys.stderr)
        return 1

    print("Valheim compatibility validation PASSED: game=%s network=%d; methods, fields, signatures, and IL counts match." %
          (args.expected_game_version, args.expected_network_version))
    return 0


if __name__ == "__main__":
    sys.exit(main())
