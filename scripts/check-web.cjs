// Checks / Browser touch workflow
// Purpose: test the embedded browser UI against an isolated demo server.
// Input: CVR_WEB_TEST_URL, including its access-key fragment; never a real device.
// State: screenshots contain demo data only; Chrome uses a temporary profile.
// Scope: desktop Chrome at tablet/phone sizes, not physical Safari validation.
const fs = require("node:fs");
const path = require("node:path");
const assert = require("node:assert/strict");
let chromium;
try {
  ({ chromium } = require("playwright"));
} catch {
  const packageBin = process.env.PATH.split(path.delimiter).find((p) =>
    fs.existsSync(path.join(p, "..", "playwright", "package.json")),
  );
  if (!packageBin) throw Error("Set NODE_PATH to a directory containing the Playwright package.");
  ({ chromium } = require(path.resolve(packageBin, "..", "playwright")));
}
(async () => {
  const browser = await chromium.launch({ channel: "chrome", headless: true });
  try {
    const page = await browser.newPage({
      viewport: { width: 1024, height: 1366 },
      hasTouch: true,
    });
    const errors = [];
    let expectedConnectionFailures = false;
    let changeRequests = 0;
    page.on("request", (r) => {
      if (r.method() === "POST" && r.url().endsWith("/api/change")) changeRequests++;
    });
    page.on("pageerror", (e) => errors.push(e.message));
    page.on("console", (m) => {
      if (m.type() === "error" && !m.text().includes("favicon") && !expectedConnectionFailures)
        errors.push(m.text());
    });
    await page.goto(process.env.CVR_WEB_TEST_URL);
    const connected = () => page.waitForFunction(() =>
      document.querySelector("#app-connection").dataset.state === "connected" &&
      !document.querySelector("#controls").disabled,
    );
    await connected();
    await page
      .locator("#devices option")
      .first()
      .waitFor({ state: "attached" });
    await page.locator('[data-tab="Inputs"]').click();
    await page.locator(".matrix button").first().waitFor();
    assert.equal(await page.locator(".matrix button").count(), 16);
    await page.locator('[data-action="delay"]').first().click();
    await page.locator("#delay-m").fill("3.43");
    assert(
      Math.abs(Number(await page.locator("#delay-ms").inputValue()) - 10) <
        0.0001,
    );
    await page.locator("#apply").click();
    await page.locator("#editor").waitFor({ state: "hidden" });
    assert(
      (
        await page.locator('[data-action="delay"]').first().textContent()
      ).includes("10 ms"),
    );
    await page.locator('[data-action="eq"]').first().click();
    await page.locator("#db-1").fill("3.5");
    await page.locator("#highPass-type").selectOption("4");
    await page.locator("#apply").click();
    await page.locator("#editor").waitFor({ state: "hidden" });
    await page.locator(".matrix button").first().click();
    await page.locator("#route-muted").uncheck();
    await page.locator("#apply").click();
    await page.locator("#editor").waitFor({ state: "hidden" });
    await page.screenshot({
      path: "artifacts/web-inputs-tablet.png",
      fullPage: true,
    });
    await page.locator('[data-tab="Outputs"]').click();
    await page.locator("[data-mode]").first().selectOption("1");
    await page.waitForFunction(
      () =>
        document.querySelector("#status").textContent === "Change confirmed",
    );
    await page.locator("#devices").selectOption("demo-2002");
    await page.waitForFunction(
      () => document.querySelectorAll("#page .card").length === 2,
    );
    await page.setViewportSize({ width: 390, height: 844 });
    assert(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= window.innerWidth,
      ),
    );
    await page.screenshot({
      path: "artifacts/web-outputs-phone.png",
      fullPage: true,
    });
    await page.setViewportSize({ width: 1024, height: 1366 });
    await page.locator('[data-tab="Inputs"]').click();
    await page.locator('[data-action="delay"]').first().click();
    await page.locator("#delay-ms").fill("12");
    const changesBeforeDisconnect = changeRequests;
    expectedConnectionFailures = true;
    await page.context().setOffline(true);
    await page.waitForFunction(() => document.querySelector("#app-connection").dataset.state === "disconnected");
    assert(await page.locator("#editor-connection").isVisible());
    assert.match(await page.locator("#editor-connection").textContent(), /App connection lost/);
    assert(await page.locator("#apply").isDisabled());
    assert(await page.locator("#delay-ms").isDisabled());
    assert(await page.locator('[value="cancel"]').isEnabled());
    await page.screenshot({ path: "artifacts/web-disconnected-tablet.png", fullPage: true });
    await page.context().setOffline(false);
    await connected();
    await page.waitForFunction(() => document.querySelector("#status").textContent.includes("Connection restored"));
    assert.equal(await page.locator("#delay-ms").inputValue(), "12");
    assert(await page.locator("#apply").isEnabled());

    // A running browser with a reachable LAN must also detect a stalled desktop app.
    const blockedHeartbeats = [];
    const stall = (route) => { blockedHeartbeats.push(route); };
    await page.route("**/api/health", stall);
    await page.waitForFunction(() => document.querySelector("#app-connection").dataset.state === "disconnected", null, { timeout: 9000 });
    assert(await page.locator("#apply").isDisabled());
    await page.unroute("**/api/health", stall);
    await Promise.allSettled(blockedHeartbeats.map((r) => r.abort()));
    await connected();

    const stoppedServer = (route) => route.abort("connectionrefused");
    await page.route("**/api/**", stoppedServer);
    await page.waitForFunction(() => document.querySelector("#app-connection").dataset.state === "disconnected");
    assert(await page.locator("#apply").isDisabled());
    await page.unroute("**/api/**", stoppedServer);
    await connected();

    const expiredKey = (route) => route.fulfill({ status: 401, body: "" });
    await page.route("**/api/health", expiredKey);
    await page.waitForFunction(() => document.querySelector("#app-connection").dataset.state === "unauthorized");
    assert.match(await page.locator("#connection-detail").textContent(), /QR code/);
    assert(await page.locator("#apply").isDisabled());
    await page.unroute("**/api/health", expiredKey);
    await connected();
    assert.equal(changeRequests, changesBeforeDisconnect, "Reconnect must not replay or submit the unfinished draft");
    assert.equal(await page.locator("#delay-ms").inputValue(), "12");
    await page.locator('[value="cancel"]').click();
    expectedConnectionFailures = false;
    assert.deepEqual(errors, []);
    console.log(
      "PASS: Chrome tablet/phone controls; offline, stalled/stopped app and expired-key detection during editing; reconnect preserves drafts without replay; no page errors or horizontal clipping",
    );
  } finally {
    await browser.close();
  }
})().catch((e) => {
  console.error(e);
  process.exitCode = 1;
});
