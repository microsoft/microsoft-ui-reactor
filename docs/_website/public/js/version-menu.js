// Keeps the header's version menu current, the way mike's menu worked.
//
// Every version is its own build, so the list a build was made with goes stale
// as new versions ship. This reads versions.json at the site root (the same
// file the older MkDocs builds read) and rebuilds the menu from it. Picking a
// version opens the same page there when it exists, else that version's home.
//
// Loaded by the script() entry in blume.config.ts, which passes the site root
// ("/microsoft-ui-reactor/") and the version of this build.
(() => {
  const me = document.currentScript;
  if (!me || window.reactorVersionMenu) return;
  window.reactorVersionMenu = true;

  const root = me.dataset.root;
  const current = me.dataset.version;
  const LATEST = "latest";
  // The row and tag classes NavSelector.astro renders, so rebuilt rows match.
  const ROW_CLASS =
    "flex w-full items-start gap-2.5 rounded-md px-2 py-1.5 text-start text-muted-foreground text-sm transition-colors hover:bg-muted hover:text-foreground aria-[current=true]:text-foreground";
  const TAG_CLASS = "rounded-full bg-muted px-1.5 py-0.5 text-[0.65rem] text-muted-foreground";

  let versions;
  const loadVersions = () =>
    (versions ??= fetch(`${root}versions.json`, { cache: "no-cache" })
      .then((response) => (response.ok ? response.json() : null))
      .catch(() => null));

  const isLatest = (entry) => entry.aliases?.includes(LATEST) ?? false;
  const folderOf = (entry) => (isLatest(entry) ? LATEST : entry.version);
  const tagOf = (entry) => (isLatest(entry) ? LATEST : entry.version === "main" ? "unreleased" : "");

  // The page's path inside its version folder: "guides/hooks/" on
  // /microsoft-ui-reactor/main/guides/hooks/.
  const pageInVersion = () => {
    if (!location.pathname.startsWith(root)) return "";
    const rest = location.pathname.slice(root.length);
    const slash = rest.indexOf("/");
    return slash === -1 ? "" : rest.slice(slash + 1);
  };

  const open = async (home) => {
    const page = pageInVersion();
    let target = home;
    if (page) {
      try {
        const response = await fetch(home + page, { method: "HEAD" });
        if (response.ok) target = home + page + location.hash;
      } catch {
        // Offline or blocked: the version's home page still works.
      }
    }
    location.assign(target);
  };

  // Another version is another build, so the client router must not swap it
  // into this page: a plain click does a full navigation instead.
  const wire = (link, home) => {
    link.setAttribute("data-astro-reload", "");
    link.addEventListener("click", (event) => {
      if (event.defaultPrevented || event.button !== 0) return;
      if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
      event.preventDefault();
      open(home);
    });
  };

  const row = (entry, check) => {
    const home = `${root}${folderOf(entry)}/`;
    const link = document.createElement("a");
    link.className = ROW_CLASS;
    link.href = home;
    const body = document.createElement("span");
    body.className = "flex-1";
    const line = document.createElement("span");
    line.className = "flex items-center gap-2";
    line.append(entry.version);
    const tag = tagOf(entry);
    if (tag) {
      const pill = document.createElement("span");
      pill.className = TAG_CLASS;
      pill.textContent = tag;
      line.append(pill);
    }
    body.append(line);
    link.append(body);
    if (entry.version === current) {
      link.setAttribute("aria-current", "true");
      if (check) link.append(check.cloneNode(true));
    }
    wire(link, home);
    return link;
  };

  const menus = () =>
    [...document.querySelectorAll("details[data-blume-dropdown]")].filter((menu) =>
      menu.querySelector(":scope > summary")?.getAttribute("aria-label")?.startsWith("Version")
    );

  const enhance = async () => {
    const found = menus();
    if (found.length === 0) return;
    // Until versions.json arrives, the build's own rows still need a full
    // navigation; they keep going to each version's home page.
    for (const menu of found) {
      for (const link of menu.querySelectorAll("[data-blume-dropdown-panel] a:not([aria-current])")) {
        wire(link, link.href);
      }
    }
    let list = await loadVersions();
    if (!Array.isArray(list) || list.length === 0) return;
    if (!list.some((entry) => entry.version === current)) list = [{ version: current, aliases: [] }, ...list];
    for (const menu of found) {
      const panel = menu.querySelector("[data-blume-dropdown-panel]");
      if (!panel?.isConnected) continue;
      const check = panel.querySelector('a[aria-current="true"] > svg:last-child');
      panel.replaceChildren(...list.map((entry) => row(entry, check)));
    }
  };

  enhance();
  document.addEventListener("astro:after-swap", enhance);
})();
