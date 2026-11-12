// Checks / Browser session integration
// Purpose: exercise authenticated HTTP edits against the shared application backend.
// Units: synthetic dB/ms/Hz values; HTTP uses an ephemeral loopback port.
// State: no real network discovery, device addresses or private presets.
// Scope: API/lifecycle and embedded assets, not physical iPad/Safari validation.
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CvrTool.Application;
using CvrTool.Web;

static class BrowserServerChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var backend = new DemoBackend(); var desktop = new AmplifierService(backend);
        await using var browser = new BrowserControlServer(desktop);
        await browser.StartAsync(IPAddress.Loopback, 0);
        var firstKey = browser.AccessKey;
        using var client = new HttpClient { BaseAddress = new(browser.Address!) };
        check((await client.GetStringAsync("/")).Contains("viewport"), "Embedded touch page is served without external assets");
        check((await client.GetAsync("/api/devices")).StatusCode == HttpStatusCode.Unauthorized, "Browser settings require the per-session access key");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", firstKey);
        var devices = await client.GetFromJsonAsync<JsonElement>("/api/devices");
        check(devices.GetArrayLength() == 3, "Browser lists the same three demo devices as Desktop");
        using (var wrongOrigin = new HttpRequestMessage(HttpMethod.Get, "/api/devices"))
        {
            wrongOrigin.Headers.Add("Origin", "http://unrelated.example");
            check((await client.SendAsync(wrongOrigin)).StatusCode == HttpStatusCode.Forbidden, "Cross-origin API requests are rejected");
        }
        using (var wrongHost = new HttpRequestMessage(HttpMethod.Get, "/"))
        {
            wrongHost.Headers.Host = "unrelated.example";
            check((await client.SendAsync(wrongHost)).StatusCode == HttpStatusCode.Forbidden, "Unexpected Host values cannot reach browser control");
        }
        var changes=0; browser.Changed += () => changes++;
        async Task<HttpResponseMessage> Post(object body) => await client.PostAsJsonAsync("/api/change", body);
        var response = await Post(new { deviceId="demo-1004",operation="input",channel=1,processing=new { gainDb=4.5,delayMs=8.89 } });
        check(response.IsSuccessStatusCode && desktop.GetInputProcessing("demo-1004",1) is { GainDb:4.5,DelayMs:8.89 } && changes==1, "Browser input changes update the authoritative Desktop session");
        desktop.Apply(desktop.Plan(new("demo-1004",2,Muted:false)).Id);
        var snapshot=await client.GetFromJsonAsync<JsonElement>("/api/state?id=demo-1004");
        check(!snapshot.GetProperty("device").GetProperty("outputs")[1].GetProperty("muted").GetBoolean(), "Desktop changes are visible through browser refresh");
        check((await Post(new { deviceId="demo-1004",operation="output",channel=2,enabled=true,value=-3.5 })).IsSuccessStatusCode && desktop.GetDevice("demo-1004").Outputs[1].Muted, "Browser output gain/mute use shared validation");
        var previous=desktop.ExportPreset("demo-1004");
        check((await Post(new { deviceId="demo-1004",operation="input",channel=1,processing=new {gainDb=500} })).StatusCode==HttpStatusCode.Conflict && desktop.ExportPreset("demo-1004")==previous,"Invalid browser gain is rejected without changing device state");
        check((await Post(new {deviceId="demo-1004",operation="factory-reset"})).StatusCode==HttpStatusCode.Conflict,"Browser exposes no factory-reset operation");
        check((await Post(new {deviceId="demo-1004",operation="load-preset",data="AA=="})).StatusCode==HttpStatusCode.Conflict,"Preset loading requires explicit confirmation");
        var file=await client.GetByteArrayAsync("/api/preset?id=demo-1004");
        check(NativePresetCodec.Decode(file).Outputs.Count==4,"Browser preset download is a valid native .sd file");
        check((await client.GetStringAsync("/app.js")).Contains("sessionStorage") && (await client.GetStringAsync("/style.css")).Contains("@media"),"Browser controls ship local scripts and responsive styles");
        await browser.DisposeAsync();
        check(browser.Address is null && browser.AccessKey=="","Stopping browser control clears address and credentials");
        await browser.StartAsync(IPAddress.Loopback,0);
        check(browser.AccessKey!=firstKey,"Restarting browser control rotates the access key");
        check(!BrowserControlServer.IsLocalAddress(IPAddress.Any) && !BrowserControlServer.IsLocalAddress(IPAddress.Parse("8.8.8.8")),"Browser listener refuses wildcard and public bind addresses");
    }
}
