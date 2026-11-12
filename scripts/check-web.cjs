// Checks / Browser touch workflow
// Purpose: test the embedded browser UI against an isolated demo server.
// Input: CVR_WEB_TEST_URL, including its access-key fragment; never a real device.
// State: screenshots contain demo data only; Chrome uses a temporary profile.
// Scope: desktop Chrome at tablet/phone sizes, not physical Safari validation.
const fs = require("node:fs");
const path = require("node:path");
const assert = require("node:assert/strict");
const packageBin = process.env.PATH.split(path.delimiter).find((p) =>
  fs.existsSync(path.join(p, "..", "playwright", "package.json")),
);
const { chromium } = require(path.resolve(packageBin, "..", "playwright"));
(async () => {
  const browser = await chromium.launch({ channel: "chrome", headless: true });
  try {
    const page = await browser.newPage({
      viewport: { width: 1024, height: 1366 },
      hasTouch: true,
    });
    const errors = [];
    page.on("pageerror", (e) => errors.push(e.message));
    page.on("console", (m) => {
      if (m.type() === "error" && !m.text().includes("favicon"))
        errors.push(m.text());
    });
    await page.goto(process.env.CVR_WEB_TEST_URL);
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
    assert.deepEqual(errors, []);
    console.log(
      "PASS: Chrome tablet/phone controls, shared delay/EQ/routing/mode edits, two/four channels, no page errors or horizontal clipping",
    );
  } finally {
    await browser.close();
  }
})().catch((e) => {
  console.error(e);
  process.exitCode = 1;
});
