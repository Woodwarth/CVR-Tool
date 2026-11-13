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
        var clock = new BrowserClock();
        await using var browser = new BrowserControlServer(desktop, clock);
        await browser.StartAsync(IPAddress.Loopback, 0);
        var firstKey = browser.AccessKey;
        using var client = new HttpClient { BaseAddress = new(browser.Address!) };
        check((await client.GetStringAsync("/")).Contains("viewport"), "Embedded touch page is served without external assets");
        check((await client.GetAsync("/api/devices")).StatusCode == HttpStatusCode.Unauthorized, "Browser settings require the per-session access key");
        check((await client.GetAsync("/api/health")).StatusCode == HttpStatusCode.Unauthorized, "Connection heartbeat requires the private session key");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", firstKey);
        client.DefaultRequestHeaders.Add("X-CVR-Client", new string('a',32));
        var flatDraft = new BrowserEqDraft([], null, null, false);
        using var curveReply = await client.PostAsJsonAsync("/api/eq-preview", flatDraft);
        var curve = await curveReply.Content.ReadFromJsonAsync<JsonElement>();
        check(curve.GetArrayLength() == 257 && curve[128].GetProperty("phase").GetDouble() == 0 &&
            curve[128].GetProperty("db").GetDouble() == 0, "Browser preview returns the shared gain and phase response");
        using var firReply = await client.PostAsJsonAsync("/api/eq-fir", flatDraft);
        var firTaps = (await firReply.Content.ReadAsStringAsync()).Split('\n');
        check(firTaps.Length == 512 && firTaps[256] == "1", "Browser exports a centered unity filter from a bypassed EQ");
        var health = await client.GetFromJsonAsync<JsonElement>("/api/health");
        check(health.GetProperty("status").GetString()=="ok" && browser.ConnectionUrl==browser.Address+"/#"+firstKey,"Authenticated heartbeat and pairing link describe the running browser session");
        check(browser.IsRunning && browser.ClientConnected,"Server status distinguishes a running listener and an active browser");
        using (var second = new HttpClient { BaseAddress = new(browser.Address!) })
        {
            second.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",firstKey);
            second.DefaultRequestHeaders.Add("X-CVR-Client",new string('b',32));
            check((int)(await second.GetAsync("/api/health")).StatusCode==423,"Second browser cannot acquire an occupied session");
            check((int)(await second.PostAsJsonAsync("/api/change",new {deviceId="demo-1004",operation="output",channel=1,value=-8})).StatusCode==423 && desktop.GetDevice("demo-1004").Outputs[0].GainDb==0,"Second browser cannot change settings");
            await second.PostAsync("/api/disconnect",null);
            check((await client.GetAsync("/api/health")).IsSuccessStatusCode,"A different browser cannot release the owner");
            await client.PostAsync("/api/disconnect",null);
            check(!browser.ClientConnected,"Explicit disconnect clears the active-browser indicator");
            check((await second.GetAsync("/api/health")).IsSuccessStatusCode,"A new browser may connect after owner disconnect");
            check((int)(await client.GetAsync("/api/devices")).StatusCode==423,"Previous browser cannot read another owner's session");
            clock.Advance(TimeSpan.FromSeconds(13));
            check((await client.GetAsync("/api/health")).IsSuccessStatusCode,"A lost browser lease expires without waiting in the test");
        }
        var devices = await client.GetFromJsonAsync<JsonElement>("/api/devices");
        check(devices.GetArrayLength() == 1, "Browser lists the same initial demo as Desktop");
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
        check(browser.Address is null && browser.AccessKey=="" && !browser.IsRunning && !browser.ClientConnected,"Stopping browser control clears address and credentials");
        await browser.StartAsync(IPAddress.Loopback,0);
        check(browser.AccessKey!=firstKey,"Restarting browser control rotates the access key");
        check((await client.GetAsync(browser.Address+"/api/health")).StatusCode==HttpStatusCode.Unauthorized,"A previous connection key cannot report a new server session as connected");
        await browser.DisposeAsync();
        await browser.StartAsync(IPAddress.Any,0);
        var localUrl = browser.ConnectionUrls.Single(url => new Uri(url).Host == "127.0.0.1");
        using var wildcardClient = new HttpClient { BaseAddress = new Uri(localUrl) };
        wildcardClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",browser.AccessKey);
        wildcardClient.DefaultRequestHeaders.Add("X-CVR-Client",new string('c',32));
        check((await wildcardClient.GetAsync("/api/health")).IsSuccessStatusCode && !browser.Address!.Contains("0.0.0.0"), "All-interface binding provides concrete authenticated pairing links");

    }
    private sealed class BrowserClock : TimeProvider
    {
        private DateTimeOffset utc=DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => utc;
        public void Advance(TimeSpan elapsed) => utc+=elapsed;
    }
}
