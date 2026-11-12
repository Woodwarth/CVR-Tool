#!/usr/bin/env python3
"""Exercise two MCP clients against one shared owner, without a --demo gate."""
import contextlib
import json
import os
import pathlib
import queue
import subprocess
import sys
import tempfile
import threading
import uuid

root = pathlib.Path(__file__).resolve().parent.parent
configuration = sys.argv[1] if len(sys.argv) > 1 else "Debug"
executable = os.environ.get("CVR_MCP_EXECUTABLE")
base = [executable] if executable else ["dotnet", str(root / f"src/CvrTool.Mcp/bin/{configuration}/net10.0/CvrTool.Mcp.dll")]
integrated = os.environ.get("CVR_MCP_INTEGRATED") == "1"
handoff = None
if os.environ.get("CVR_MCP_HANDOFF") == "1":
    handoff = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(1)"], stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    threading.Thread(target=handoff.wait, daemon=True).start()
    base += ["--finish-update", str(handoff.pid)]
session = "cvr-check-" + uuid.uuid4().hex

class Client:
    def __init__(self, errors):
        command = base + (["--mcp"] if integrated else []) + ["--session", session]
        self.process = subprocess.Popen(command, cwd=root, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=errors, text=True, encoding="utf-8", bufsize=1)
        self.queue = queue.Queue()
        self.number = 0
        def read():
            for line in self.process.stdout:
                self.queue.put(line)
            self.queue.put(None)
        threading.Thread(target=read, daemon=True).start()
        self.request("initialize", {"protocolVersion": "2025-11-25", "capabilities": {},
            "clientInfo": {"name": "cvr-check", "version": "0.3.0"}})
        self.send({"jsonrpc": "2.0", "method": "notifications/initialized"})
    def send(self, value):
        self.process.stdin.write(json.dumps(value) + "\n")
        self.process.stdin.flush()
    def request(self, method, params):
        self.number += 1
        self.send({"jsonrpc": "2.0", "id": self.number, "method": method, "params": params})
        while True:
            line = self.queue.get(timeout=30)
            if line is None:
                raise RuntimeError("MCP process exited before replying")
            response = json.loads(line)
            if response.get("id") == self.number:
                if "error" in response:
                    raise RuntimeError(str(response["error"]))
                return response["result"]
    def call(self, tool_name, **arguments):
        result = self.request("tools/call", {"name": tool_name, "arguments": arguments})
        if result.get("isError"):
            raise RuntimeError(str(result))
        return json.loads("".join(c.get("text", "") for c in result["content"] if c["type"] == "text"))
    def reject(self, tool_name, **arguments):
        assert self.request("tools/call", {"name": tool_name, "arguments": arguments}).get("isError"), tool_name
    def close(self):
        self.process.terminate()
        try:
            self.process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.wait()

with tempfile.TemporaryFile(mode="w+t", encoding="utf-8") as errors, contextlib.ExitStack() as cleanup:
    owner = subprocess.Popen(base + ["--host-session", "--session", session], cwd=root,
        stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=errors)
    def stop_owner():
        owner.terminate()
        try:
            owner.wait(timeout=5)
        except subprocess.TimeoutExpired:
            owner.kill()
            owner.wait()
    cleanup.callback(stop_owner)
    try:
        a = Client(errors); cleanup.callback(a.close)
        b = Client(errors); cleanup.callback(b.close)
        names = {tool["name"] for tool in a.request("tools/list", {})["tools"]}
        expected = {"list_devices", "get_device", "plan_channel_change", "apply_change", "export_preset", "get_native_preset_metadata",
            "rename_device", "get_routing", "get_telemetry", "plan_routing_change", "apply_routing_change",
            "connect_amplifier", "disconnect_amplifier", "get_device_controls", "set_device_controls", "set_device_wink", "get_input_processing", "plan_input_processing", "apply_input_processing"}
        assert expected <= names, names
        a.reject("connect_amplifier", address="localhost", localAddress="127.0.0.1")
        assert a.call("set_device_wink", deviceId="demo-2002", enabled=True)["Wink"] is True
        assert b.call("get_device_controls", deviceId="demo-2002")["Wink"] is True
        assert b.call("set_device_wink", deviceId="demo-2002", enabled=False)["Wink"] is False
        assert a.call("get_native_preset_metadata", deviceId="demo-2002") is None
        devices = a.call("list_devices")
        assert {len(d["Outputs"]) for d in devices} == {2, 4}
        assert all(d["Capabilities"]["InputCount"] == len(d["Outputs"]) for d in devices)
        for device in devices:
            routing = a.call("get_routing", deviceId=device["Id"])
            assert len(routing["Cells"]) == len(device["Outputs"]) ** 2
        plan = a.call("plan_channel_change", deviceId="demo-2002", channel=2, gainDb=-6)
        assert b.call("get_device", deviceId="demo-2002")["Outputs"][1]["GainDb"] == 0
        a.call("apply_change", planId=plan["Id"])
        assert b.call("get_device", deviceId="demo-2002")["Outputs"][1]["GainDb"] == -6
        a.reject("apply_change", planId=plan["Id"])
        route = b.call("plan_routing_change", deviceId="demo-2002", input=2, output=1, gainDb=-80, muted=False)
        b.call("apply_routing_change", planId=route["Id"])
        routed = a.call("get_routing", deviceId="demo-2002")
        assert next(c for c in routed["Cells"] if c["Input"] == 2 and c["Output"] == 1)["GainDb"] == -80
        input_gain=b.call("plan_input_processing",deviceId="demo-2002",channel=1,gainDb=4.5)
        b.call("apply_input_processing",planId=input_gain["Id"])
        assert a.call("get_input_processing",deviceId="demo-2002",channel=1)["GainDb"]==4.5
        assert a.call("get_device",deviceId="demo-2002")["Outputs"][0]["GainDb"]==0
        input_mute=b.call("plan_input_processing",deviceId="demo-2002",channel=1,muted=True)
        b.call("apply_input_processing",planId=input_mute["Id"])
        assert a.call("get_input_processing",deviceId="demo-2002",channel=1)["Muted"] is True
        assert a.call("set_output_fir_enabled",deviceId="demo-2002",channel=1,enabled=True)["Enabled"] is True
        assert b.call("set_output_fir_enabled",deviceId="demo-2002",channel=1,enabled=False)["Enabled"] is False
        before = a.call("get_input_processing", deviceId="demo-2002", channel=1)
        bands = before["Bands"]
        bands[0]["GainDb"] = 3
        input_plan = b.call("plan_input_processing", deviceId="demo-2002", channel=1, delayMs=10, eqEnabled=False, bands=bands)
        assert a.call("get_input_processing", deviceId="demo-2002", channel=1)["DelayMs"] == 0
        stale = a.call("plan_channel_change", deviceId="demo-2002", channel=1, gainDb=-2)
        b.call("apply_input_processing", planId=input_plan["Id"])
        after = a.call("get_input_processing", deviceId="demo-2002", channel=1)
        assert after["DelayMs"] == 10 and not after["EqEnabled"] and after["Bands"][0]["GainDb"] == 3
        a.reject("apply_change", planId=stale["Id"])
        b.reject("apply_input_processing", planId=input_plan["Id"])
        crossover = b.call("plan_input_processing", deviceId="demo-2002", channel=1,
            highPass={"FrequencyHz":80,"FilterType":6,"Enabled":True},lowPass={"FrequencyHz":16000,"FilterType":9,"Enabled":False})
        b.call("apply_input_processing",planId=crossover["Id"])
        processed = a.call("get_input_processing",deviceId="demo-2002",channel=1)
        assert processed["HighPass"] == {"FrequencyHz":80,"FilterType":6,"Enabled":True}
        assert processed["LowPass"] == {"FrequencyHz":16000,"FilterType":9,"Enabled":False}
        a.call("rename_device", deviceId="demo-2002", name="Front amp")
        assert b.call("get_device", deviceId="demo-2002")["Name"] == "Front amp"
        a.call("set_device_controls", deviceId="demo-2002", displayLocked=True, standby=True)
        assert b.call("get_device_controls", deviceId="demo-2002") == {"DisplayLocked": True, "Standby": True, "StandbyKnown": True, "Wink": False}
        telemetry = a.call("get_telemetry", deviceId="demo-2002")
        assert len(telemetry) == 2 and all(t["Voltage"] is None for t in telemetry)
        exported = b.call("export_preset", deviceId="demo-2002")
        assert exported["SchemaVersion"] == 7 and exported["Name"] == "Front amp" and len(exported["Inputs"]) == 2
        mode_plan=a.call("plan_output_dynamics",deviceId="demo-2002",channel=1,loadMode=2)
        a.call("apply_output_dynamics",planId=mode_plan["Id"])
        assert b.call("get_output_dynamics",deviceId="demo-2002",channel=1)["LoadMode"]==2
        assert b.call("get_output_dynamics",deviceId="demo-2002",channel=2)["LoadMode"]==0
        dynamics=a.call("get_output_dynamics",deviceId="demo-2002",channel=1)
        dynamics["Rms"].update(ThresholdVolts=60,AttackMs=40,ReleaseMultiplier=8,Automatic=True)
        dynamics["Peak"].update(ThresholdVolts=100,HoldMs=80,ReleaseMs=400)
        dyn_plan=a.call("plan_output_dynamics",deviceId="demo-2002",channel=1,bridged=True,noiseGateEnabled=True,noiseGateDb=-70,rms=dynamics["Rms"],peak=dynamics["Peak"])
        a.call("apply_output_dynamics",planId=dyn_plan["Id"])
        assert b.call("get_output_dynamics",deviceId="demo-2002",channel=2)["Bridged"]
        actual=b.call("get_output_dynamics",deviceId="demo-2002",channel=1)
        assert actual["NoiseGateDb"]==-70 and actual["Rms"]["ThresholdVolts"]==60 and actual["Peak"]["HoldMs"]==80
        a.reject("apply_output_dynamics",planId=dyn_plan["Id"])
        out_plan = a.call("plan_output_processing", deviceId="demo-2002", channel=1, delayMs=4,
            highPass={"FrequencyHz":100,"FilterType":6,"Enabled":True})
        a.call("apply_output_processing", planId=out_plan["Id"])
        assert b.call("get_output_processing", deviceId="demo-2002", channel=1)["HighPass"]["FrequencyHz"] == 100
        a.call("set_output_fir", deviceId="demo-2002", channel=1, name="Test delay", coefficients=[0,1], enabled=True)
        assert b.call("get_output_fir", deviceId="demo-2002", channel=1)["Coefficients"] == [0,1]
        a.reject("store_device_preset", deviceId="demo-2002", slot=16, name="Last slot")
        a.call("store_device_preset", deviceId="demo-2002", slot=16, name="Last slot", confirmed=True)
        assert b.call("get_device_presets", deviceId="demo-2002")["Slots"][15]["Name"] == "Last slot"
        native = b.call("export_native_preset", deviceId="demo-2002")
        a.call("load_preset_file", deviceId="demo-2002", fileName="preset.sd", base64=native["Base64"], confirmed=True)
        assert len(b.call("get_native_preset_metadata", deviceId="demo-2002")["Channels"]) == 2
        a.call("recall_device_preset", deviceId="demo-2002", slot=16, confirmed=True)
        b.call("clear_device_presets", deviceId="demo-2002", confirmed=True)
        assert not any(s["Occupied"] for s in a.call("get_device_presets",deviceId="demo-2002")["Slots"])
        a.reject("factory_reset", deviceId="demo-2002")
        b.call("factory_reset", deviceId="demo-2002", confirmed=True)
        assert not a.call("get_output_fir", deviceId="demo-2002", channel=1)["Enabled"]
        if handoff is not None:
            handoff.wait(timeout=5)
            print("PASS: updated executable waits for the previous process and completes the update handoff")
        print("PASS: MCP without demo flag, two shared clients, uniform models, channel/routing/input edits, revision conflicts, rename, controls full JSON/native presets, output EQ/FIR and sixteen device slots")
    except Exception:
        errors.seek(0)
        print(errors.read(), file=sys.stderr)
        raise
