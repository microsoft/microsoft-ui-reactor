import { describe, test } from "node:test";
import assert from "node:assert/strict";

import { folderOf, homeOf, menuEntries, pageInVersion, tagOf } from "../public/js/version-paths.js";

const root = "/microsoft-ui-reactor/";
const latest = { version: "0.1.0-preview.18", aliases: ["latest"] };
const older = { version: "0.1.0-preview.17", aliases: [] };
const main = { version: "main", aliases: [] };

describe("version menu paths", () => {
  test("the latest release lives in /latest/, every other version in its own folder", () => {
    assert.equal(folderOf(latest), "latest");
    assert.equal(folderOf(older), "0.1.0-preview.17");
    assert.equal(homeOf(root, latest), "/microsoft-ui-reactor/latest/");
    assert.equal(homeOf(root, main), "/microsoft-ui-reactor/main/");
  });

  test("tags mark the latest release and main", () => {
    assert.equal(tagOf(latest), "latest");
    assert.equal(tagOf(main), "unreleased");
    assert.equal(tagOf(older), "");
    assert.equal(tagOf({ version: "0.1.0-preview.12" }), "");
  });

  test("the page path drops the site root and the version folder", () => {
    assert.equal(pageInVersion("/microsoft-ui-reactor/main/guides/hooks/", root), "guides/hooks/");
    assert.equal(pageInVersion("/microsoft-ui-reactor/latest/", root), "");
    assert.equal(pageInVersion("/microsoft-ui-reactor/latest", root), "");
    assert.equal(pageInVersion("/microsoft-ui-reactor/", root), "");
    assert.equal(pageInVersion("/elsewhere/main/guides/", root), "");
  });

  test("the menu keeps the build's own rows when versions.json is unusable", () => {
    assert.equal(menuEntries(null, "main"), null);
    assert.equal(menuEntries([], "main"), null);
    assert.equal(menuEntries({ version: "main" }, "main"), null);
  });

  test("a build newer than versions.json still lists itself, first", () => {
    const list = [latest, older];
    assert.equal(menuEntries(list, "0.1.0-preview.17"), list);
    assert.deepEqual(
      menuEntries(list, "main").map((entry) => entry.version),
      ["main", "0.1.0-preview.18", "0.1.0-preview.17"]
    );
  });
});
