async function api(url, options) {
    const response = await fetch(url, {
        headers: { "Content-Type": "application/json" },
        ...options
    });
    if (!response.ok) {
        const text = await response.text();
        throw new Error(text || response.statusText);
    }
    if (response.status === 204) return null;
    return await response.json();
}

async function apiWithResponse(url, options) {
    const response = await fetch(url, {
        headers: { "Content-Type": "application/json" },
        ...options
    });
    const text = await response.text();
    let body = null;
    if (text) {
        try {
            body = JSON.parse(text);
        } catch {
            body = text;
        }
    }

    if (!response.ok) {
        const message = typeof body === "string"
            ? body
            : (body?.error || body?.message || response.statusText);
        throw new Error(message);
    }

    return { response, body };
}

function initShell() {
    const shell = document.querySelector(".app-shell");
    const toggle = document.getElementById("sidebar-toggle");
    const path = window.location.pathname === "/" ? "/" : window.location.pathname.replace(/\/$/, "");
    document.querySelectorAll(".side-nav a").forEach(link => {
        const route = link.dataset.route;
        const active = route === "/" ? path === "/" : path.startsWith(route);
        link.classList.toggle("active", active);
    });
    if (localStorage.getItem("livingLoreSidebar") === "collapsed") shell?.classList.add("sidebar-collapsed");
    toggle?.addEventListener("click", () => {
        shell?.classList.toggle("sidebar-collapsed");
        localStorage.setItem("livingLoreSidebar", shell?.classList.contains("sidebar-collapsed") ? "collapsed" : "expanded");
    });
}

function renderTable(id, columns, rows) {
    const table = document.getElementById(id);
    if (!table) return;
    rows = rows || [];
    let state = table._state || { page: 1, sortIndex: null, sortDirection: 1, query: "" };
    table._state = state;

    if (!table.parentElement.classList.contains("table-wrap")) {
        const panel = document.createElement("div");
        panel.className = "table-panel";
        const tools = document.createElement("div");
        tools.className = "table-tools";
        tools.innerHTML = `<input class="table-search" placeholder="Search records"><span class="table-count"></span>`;
        const wrap = document.createElement("div");
        wrap.className = "table-wrap";
        const pager = document.createElement("div");
        pager.className = "table-tools table-pager";
        table.parentNode.insertBefore(panel, table);
        panel.appendChild(tools);
        panel.appendChild(wrap);
        wrap.appendChild(table);
        panel.appendChild(pager);
        tools.querySelector(".table-search").addEventListener("input", event => {
            state.query = event.target.value;
            state.page = 1;
            draw();
        });
    }

    const panel = table.closest(".table-panel");
    const search = panel.querySelector(".table-search");
    if (search.value !== state.query) search.value = state.query;

    function cellText(row, column) {
        if (column.key) return String(row[column.key] ?? "");
        if (!column.render) return "";
        const value = column.render(row);
        return value instanceof Node ? value.textContent ?? "" : String(value ?? "");
    }

    function filteredRows() {
        const query = state.query.trim().toLowerCase();
        let data = query
            ? rows.filter(row => columns.some(column => cellText(row, column).toLowerCase().includes(query)))
            : rows.slice();
        if (state.sortIndex !== null) {
            const column = columns[state.sortIndex];
            data.sort((a, b) => cellText(a, column).localeCompare(cellText(b, column), undefined, { numeric: true }) * state.sortDirection);
        }
        return data;
    }

    function draw() {
        const pageSize = 12;
        const data = filteredRows();
        const maxPage = Math.max(1, Math.ceil(data.length / pageSize));
        state.page = Math.min(state.page, maxPage);
        const pageRows = data.slice((state.page - 1) * pageSize, state.page * pageSize);

    table.innerHTML = "";
    const thead = table.createTHead();
    const headerRow = thead.insertRow();
        for (const [index, column] of columns.entries()) {
        const th = document.createElement("th");
            th.textContent = column.label + (state.sortIndex === index ? (state.sortDirection === 1 ? " +" : " -") : "");
            th.addEventListener("click", () => {
                if (state.sortIndex === index) state.sortDirection *= -1;
                else {
                    state.sortIndex = index;
                    state.sortDirection = 1;
                }
                draw();
            });
        headerRow.appendChild(th);
    }
    const tbody = table.createTBody();
        for (const row of pageRows) {
        const tr = tbody.insertRow();
        for (const column of columns) {
            const td = tr.insertCell();
            const value = column.render ? column.render(row) : row[column.key];
            if (value instanceof Node) td.appendChild(value);
            else td.textContent = value ?? "";
        }
    }
        if (pageRows.length === 0) {
            const tr = tbody.insertRow();
            const td = tr.insertCell();
            td.colSpan = columns.length;
            td.innerHTML = `<div class="empty-state"><div class="empty-icon">NO</div><p>No records found. Adjust search or run the relevant scan/action.</p></div>`;
        }

        panel.querySelector(".table-count").textContent = `${data.length} record(s)`;
        const pager = panel.querySelector(".table-pager");
        pager.innerHTML = "";
        const prev = document.createElement("button");
        prev.className = "secondary";
        prev.textContent = "Previous";
        prev.disabled = state.page <= 1;
        prev.onclick = () => { state.page--; draw(); };
        const next = document.createElement("button");
        next.className = "secondary";
        next.textContent = "Next";
        next.disabled = state.page >= maxPage;
        next.onclick = () => { state.page++; draw(); };
        const label = document.createElement("span");
        label.className = "chip";
        label.textContent = `Page ${state.page} of ${maxPage}`;
        pager.append(prev, label, next);
    }

    draw();
}

function formJson(form) {
    const data = new FormData(form);
    const json = {};
    for (const [key, value] of data.entries()) json[key] = value;
    for (const input of form.querySelectorAll("input[type='number']")) json[input.name] = Number(input.value);
    for (const input of form.querySelectorAll("input[type='checkbox']")) json[input.name] = input.checked;
    return json;
}

async function loadDashboard() {
    const dashboard = await api("/api/dashboard");
    document.getElementById("metrics").innerHTML = `
        <div class="metric"><span>Database Status</span><strong>${dashboard.databaseStatus}</strong><small>${dashboard.databasePath}</small></div>
        <div class="metric"><span>Active Characters</span><strong>${dashboard.activeCharacters}</strong><small>${dashboard.inactiveCharacters} inactive profiles retained</small></div>
        <div class="metric"><span>Detected Mods</span><strong>${dashboard.detectedMods}</strong><small>Active scan sources</small></div>
        <div class="metric"><span>Lore Conflicts</span><strong>${dashboard.conflictsFound}</strong><small>Awaiting review</small></div>
        <div class="metric"><span>Active Player Profile</span><strong>${dashboard.activePlayerProfile ? escapeHtml(dashboard.activePlayerProfile.profileName) : "None"}</strong><small>${dashboard.activePlayerProfile ? `${escapeHtml(dashboard.activePlayerProfile.farmerName)} · ${escapeHtml(dashboard.activePlayerProfile.farmName)}${dashboard.activePlayerProfile.linkedSaveFile ? ` · save: ${escapeHtml(dashboard.activePlayerProfile.linkedSaveFile)}` : ""}` : "Set one on Player Profiles"}</small></div>`;
    const profileDetail = dashboard.activePlayerProfile
        ? `${escapeHtml(dashboard.activePlayerProfile.farmerName)} - ${escapeHtml(dashboard.activePlayerProfile.farmName)}${dashboard.activePlayerProfile.linkedSaveFile ? ` - save: ${escapeHtml(dashboard.activePlayerProfile.linkedSaveFile)}` : ""}`
        : "Set one on Player Profiles";
    document.getElementById("metrics").innerHTML = `
        <div class="metric"><span>Ledger Status</span><strong>${dashboard.databaseStatus}</strong><small>${dashboard.databasePath}</small></div>
        <div class="metric"><span>Living Cast</span><strong>${dashboard.activeCharacters}</strong><small>${dashboard.inactiveCharacters} archived profiles retained</small></div>
        <div class="metric"><span>Mod Tomes</span><strong>${dashboard.detectedMods}</strong><small>Active scan sources</small></div>
        <div class="metric"><span>Conflict Runes</span><strong>${dashboard.conflictsFound}</strong><small>Awaiting review</small></div>
        <div class="metric"><span>Farmer Profile</span><strong>${dashboard.activePlayerProfile ? escapeHtml(dashboard.activePlayerProfile.profileName) : "None"}</strong><small>${profileDetail}</small></div>`;
    renderDialogueCards("dialogue-cards", dashboard.recentGeneratedDialogue);
    renderDashboardCharts(dashboard);
    renderTable("changes", [
        { label: "When", key: "timestamp" },
        { label: "Character", key: "characterId" },
        { label: "Field", key: "fieldChanged" },
        { label: "New Value", key: "newValue" }
    ], dashboard.recentLoreChanges);
}

function renderDialogueCards(id, rows) {
    const container = document.getElementById(id);
    if (!container) return;
    if (!rows || rows.length === 0) {
        container.innerHTML = `<div class="empty-state"><div class="empty-icon">DL</div><p>No spoken lines in the ledger yet. Use Dialogue Test to conjure the first one.</p></div>`;
        return;
    }
    container.innerHTML = rows.slice(0, 6).map(row => `
        <article class="dialogue-card">
            <div class="card-header">
                <div>
                    <strong>${escapeHtml(row.characterName)}</strong>
                    <span class="chip">${escapeHtml(row.topic || "general")}</span>
                </div>
                <small>${escapeHtml(row.createdDate || "")}</small>
            </div>
            <p>${escapeHtml(row.dialogueText || "")}</p>
            <div class="action-row">
                ${link(`/DialogueExplanation?id=${row.id}`, "Explain").outerHTML}
                ${link("/DialogueOverrides", "Review").outerHTML}
                ${link("/DialogueTest", "Regenerate").outerHTML}
            </div>
        </article>`).join("");
}

function renderDashboardCharts(dashboard) {
    const container = document.getElementById("dashboard-charts");
    if (!container) return;
    const max = Math.max(1, dashboard.activeCharacters, dashboard.inactiveCharacters, dashboard.detectedMods, dashboard.conflictsFound);
    const rows = [
        ["Living Cast", dashboard.activeCharacters],
        ["Archived Cast", dashboard.inactiveCharacters],
        ["Mod Tomes", dashboard.detectedMods],
        ["Conflict Runes", dashboard.conflictsFound]
    ];
    container.innerHTML = rows.map(([label, value]) => `
        <div class="chart-bar">
            <span>${label}</span>
            <span style="width:${Math.max(4, (value / max) * 100)}%"></span>
            <strong>${value}</strong>
        </div>`).join("");
}

async function loadOpenAiPanel() {
    const select = document.getElementById("openai-model-select");
    if (!select) return;

    try {
        const models = await api("/api/openai/models");
        const options = models.available.slice();
        if (models.current && !options.includes(models.current)) options.unshift(models.current);
        select.innerHTML = "";
        for (const model of options) {
            const opt = document.createElement("option");
            opt.value = model;
            opt.textContent = model;
            if (model === models.current) opt.selected = true;
            select.appendChild(opt);
        }
    } catch (error) {
        // Leave the selector empty; status will surface the problem.
    }

    bindOpenAiModelControls();
    await refreshOpenAiStatus();
}

async function refreshOpenAiStatus() {
    const statusEl = document.getElementById("openai-status");
    if (!statusEl) return;
    statusEl.innerHTML = `<div class="metric"><span>API Status</span><strong>Checking…</strong></div>`;
    try {
        const s = await api("/api/openai/status");
        const state = !s.hasApiKey ? "No API key" : (s.connected ? "Connected" : "Not connected");
        const cls = !s.hasApiKey ? "warn" : (s.connected ? "ok" : "bad");
        statusEl.innerHTML = `
            <div class="metric"><span>API Status</span><strong class="conn-${cls}">${state}</strong>${s.error ? `<small>${s.error}</small>` : ""}</div>
            <div class="metric"><span>Active Model</span><strong>${s.model}</strong></div>`;
    } catch (error) {
        statusEl.innerHTML = `<div class="metric"><span>API Status</span><strong class="conn-bad">Error</strong><small>${error.message}</small></div>`;
    }
}

function bindOpenAiModelControls() {
    const apply = document.getElementById("openai-model-apply");
    const recheck = document.getElementById("openai-recheck");
    const result = document.getElementById("openai-model-result");

    if (apply && !apply.dataset.bound) {
        apply.dataset.bound = "1";
        apply.addEventListener("click", async () => {
            const model = document.getElementById("openai-model-select").value;
            result.className = "status";
            result.textContent = "Saving…";
            try {
                await api("/api/openai/model", { method: "POST", body: JSON.stringify({ model }) });
                result.className = "status success";
                result.textContent = `Model set to ${model}.`;
                await refreshOpenAiStatus();
            } catch (error) {
                result.className = "status error";
                result.textContent = error.message;
            }
        });
    }

    if (recheck && !recheck.dataset.bound) {
        recheck.dataset.bound = "1";
        recheck.addEventListener("click", refreshOpenAiStatus);
    }
}

async function loadCharacters() {
    const toggle = document.getElementById("characters-include-inactive");
    const includeInactive = toggle ? toggle.checked : false;
    const characters = await api(`/api/characters?includeInactive=${includeInactive}`);
    const count = document.getElementById("characters-count");
    if (count) count.textContent = `${characters.length} character(s) shown${includeInactive ? " (including historical/inactive)" : " (active only)"}`;
    renderTable("characters", [
        { label: "Name", render: row => link(`/Character/${row.id}`, row.name) },
        { label: "Status", render: row => row.isActive ? "active" : "inactive" },
        { label: "Source Mod", key: "sourceModName" },
        { label: "Kind", key: "kind" }
    ], characters);
}

async function loadValidation() {
    const data = await api("/api/validation");

    const counts = document.getElementById("validation-counts");
    if (counts) {
        counts.innerHTML = `
            <div class="metric"><span>Confirmed</span><strong>${data.counts.confirmed}</strong></div>
            <div class="metric"><span>Probable</span><strong>${data.counts.probable}</strong></div>
            <div class="metric"><span>Rejected</span><strong>${data.counts.rejected}</strong></div>`;
    }

    const columns = [
        { label: "Name", key: "name" },
        { label: "Source Mod", key: "sourceModName" },
        { label: "Score", render: row => `${row.score}/100` },
        { label: "Imported", render: row => row.imported ? "yes" : "no" },
        { label: "Evidence", render: row => (row.evidence || []).join(", ") || "none" },
        { label: "Validation Rules", render: row => renderRules(row.rules) }
    ];

    renderTable("validation-confirmed", columns, data.confirmed);
    renderTable("validation-probable", columns, data.probable);
    renderTable("validation-rejected", columns, data.rejected);
}

function renderRules(rules) {
    const list = document.createElement("ul");
    list.className = "rule-list";
    for (const rule of rules || []) {
        const item = document.createElement("li");
        item.className = rule.passed ? "rule-pass" : "rule-fail";
        item.textContent = `${rule.passed ? "✓" : "✗"} ${rule.name} (+${rule.points})`;
        list.appendChild(item);
    }
    return list;
}

function bindClearCharacters() {
    const button = document.getElementById("clear-characters");
    if (!button) return;
    button.addEventListener("click", async () => {
        if (!confirm("Delete ALL characters? This also removes their relationships, memories, overrides, and history so you can do a fresh rescan.")) return;
        const result = document.getElementById("clear-result");
        button.disabled = true;
        button.textContent = "Clearing...";
        result.className = "status";
        result.textContent = "Clearing characters...";
        try {
            const summary = await api("/api/characters", { method: "DELETE" });
            result.className = "status success";
            result.textContent = `Cleared ${summary.charactersDeleted} detected character(s) and ${summary.canonicalCharactersDeleted} canonical profile(s). Run a scan to repopulate.`;
            await loadCharacters();
        } catch (error) {
            result.className = "status error";
            result.textContent = error.message;
        } finally {
            button.disabled = false;
            button.textContent = "Clear All Characters";
        }
    });
}

async function loadCharacterDetail(id) {
    const detail = await api(`/api/characters/${id}`);
    document.getElementById("character-title").textContent = detail.character.name;
    const sourceMods = (detail.characterSources || [])
        .map(source => `${source.sourceModId} (${source.sourceType}, priority ${source.priority})`)
        .join("; ") || (detail.character.sourceModName ?? "Vanilla or custom");
    const initials = (detail.character.name || "?").slice(0, 2).toUpperCase();
    document.getElementById("character-detail").innerHTML = `
        <section class="profile-hero">
            <div class="portrait-placeholder">${escapeHtml(initials)}</div>
            <div>
                <p class="eyebrow">Canonical Character Profile</p>
                <h2>${escapeHtml(detail.character.name)}</h2>
                <div class="action-row">
                    <span class="badge ${detail.character.isActive ? "success" : "warning"}">${detail.character.isActive ? "Active" : "Inactive"}</span>
                    <span class="badge">${escapeHtml(detail.kind)}</span>
                    <span class="badge">${detail.character.canonicalCharacterId ? "Canonical linked" : "Standalone"}</span>
                </div>
                <p><strong>Source mods:</strong> ${escapeHtml(sourceMods)}</p>
            </div>
        </section>
        <section class="output">
            <div class="tabs" role="tablist">
                ${["Overview", "Voice Profile", "Dialogue Sources", "Relationships", "Memories", "Overrides", "History"].map((name, index) =>
                    `<button type="button" class="tab-button ${index === 0 ? "active" : ""}" data-tab="char-tab-${index}">${name}</button>`).join("")}
            </div>
            <div id="char-tab-0" class="tab-panel">
                <h3>Overview</h3>
                <p><strong>Personality:</strong> ${escapeHtml(detail.character.personality)}</p>
                <p><strong>Occupation:</strong> ${escapeHtml(detail.character.occupation)}</p>
                <p><strong>Home:</strong> ${escapeHtml(detail.character.homeLocation)}</p>
                <h4>Detected Profile Instances</h4><pre>${escapeHtml(JSON.stringify(detail.characterInstances ?? [], null, 2))}</pre>
            </div>
            <div id="char-tab-1" class="tab-panel" hidden>
                <h3>Voice Profile</h3>
                <p>${escapeHtml(detail.voiceRules.map(x => x.ruleText).join("; ") || "No explicit voice rules stored yet.")}</p>
            </div>
            <div id="char-tab-2" class="tab-panel" hidden>
                <h3>Dialogue Sources And Mod Scan Metadata</h3>
                <pre>${escapeHtml(JSON.stringify({ sources: detail.characterSources ?? [], metadata: detail.modScanMetadata }, null, 2))}</pre>
            </div>
            <div id="char-tab-3" class="tab-panel" hidden><h3>Relationships</h3><pre>${escapeHtml(JSON.stringify(detail.relationships, null, 2))}</pre></div>
            <div id="char-tab-4" class="tab-panel" hidden><h3>Memories</h3><pre>${escapeHtml(JSON.stringify(detail.memories, null, 2))}</pre></div>
            <div id="char-tab-5" class="tab-panel" hidden><h3>User Overrides</h3><pre>${escapeHtml(JSON.stringify(detail.userOverrides, null, 2))}</pre></div>
            <div id="char-tab-6" class="tab-panel" hidden><h3>Dialogue History</h3><pre>${escapeHtml(JSON.stringify(detail.dialogueHistory, null, 2))}</pre></div>
        </section>`;
    bindTabs(document.getElementById("character-detail"));
}

function bindTabs(root) {
    root.querySelectorAll(".tab-button").forEach(button => {
        button.addEventListener("click", () => {
            root.querySelectorAll(".tab-button").forEach(item => item.classList.remove("active"));
            root.querySelectorAll(".tab-panel").forEach(panel => panel.hidden = true);
            button.classList.add("active");
            root.querySelector(`#${button.dataset.tab}`).hidden = false;
        });
    });
}

function bindOverrideForm(id) {
    document.getElementById("override-form").addEventListener("submit", async event => {
        event.preventDefault();
        await api(`/api/characters/${id}/overrides`, { method: "POST", body: JSON.stringify(formJson(event.target)) });
        await loadCharacterDetail(id);
        event.target.reset();
    });
}

async function loadMemories() {
    const filter = document.getElementById("memory-filter-form");
    const params = filter ? new URLSearchParams(formJson(filter)) : new URLSearchParams();
    if (filter && filter.elements.namedItem("includeInactive").checked)
        params.set("includeInactive", "true");
    else
        params.set("includeInactive", "false");
    for (const [key, value] of [...params.entries()]) {
        if (value === "" || value == null)
            params.delete(key);
    }
    const memories = await api(`/api/memories?${params.toString()}`);
    renderTable("memories", [
        { label: "ID", key: "id" },
        { label: "Save", key: "saveFileName" },
        { label: "Profile", key: "playerProfileId" },
        { label: "NPC", key: "npcName" },
        { label: "Type", key: "memoryType" },
        { label: "Source", key: "source" },
        { label: "Character ID", key: "characterId" },
        { label: "Importance", key: "importance" },
        { label: "Title", key: "title" },
        { label: "Memory", key: "summary" },
        { label: "In-game Date", render: row => `${row.year || 0} ${row.season || ""} ${row.day || 0}`.trim() },
        { label: "Location", key: "location" },
        { label: "Created", key: "createdDate" },
        { label: "Active", render: row => row.isActive ? "Yes" : "No" },
        { label: "Action", render: row => {
            const wrap = document.createElement("div");
            wrap.className = "button-row";
            const edit = document.createElement("button");
            edit.className = "secondary";
            edit.textContent = "Edit";
            edit.onclick = () => {
                const form = document.getElementById("memory-form");
                for (const name of ["id", "characterId", "saveFileName", "playerName", "farmName", "playerProfileId", "npcName", "memoryType", "title", "importance", "season", "day", "year", "location", "tags", "summary"]) {
                    const field = form.elements.namedItem(name);
                    if (field)
                        field.value = row[name] ?? "";
                }
                form.elements.namedItem("isActive").checked = !!row.isActive;
            };
            const deactivate = document.createElement("button");
            deactivate.className = "secondary";
            deactivate.textContent = "Deactivate";
            deactivate.onclick = async () => {
                await api(`/api/memories/${row.id}/deactivate`, { method: "POST" });
                await loadMemories();
            };
            const del = document.createElement("button");
            del.className = "secondary";
            del.textContent = "Delete";
            del.onclick = async () => {
                await api(`/api/memories/${row.id}`, { method: "DELETE" });
                await loadMemories();
            };
            wrap.append(edit, deactivate, del);
            return wrap;
        }}
    ], memories);
}

function bindMemoryForm() {
    const filter = document.getElementById("memory-filter-form");
    if (filter) {
        filter.addEventListener("submit", async event => {
            event.preventDefault();
            await loadMemories();
        });
    }

    document.getElementById("memory-form").addEventListener("submit", async event => {
        event.preventDefault();
        const json = formJson(event.target);
        const id = json.id;
        delete json.id;
        json.memoryText = json.summary;
        json.source = json.source || "Manual";
        json.isActive = event.target.elements.namedItem("isActive").checked;
        await api(id ? `/api/memories/${id}` : "/api/memories", {
            method: id ? "PUT" : "POST",
            body: JSON.stringify(json)
        });
        event.target.reset();
        await loadMemories();
    });
}

async function loadRelationships() {
    const relationships = await api("/api/relationships");
    renderTable("relationships", [
        { label: "ID", key: "id" },
        { label: "Character A", key: "characterA" },
        { label: "Character B", key: "characterB" },
        { label: "Type", key: "relationshipType" },
        { label: "Strength", key: "strength" },
        { label: "Action", render: row => {
            const button = document.createElement("button");
            button.className = "secondary";
            button.textContent = "Edit";
            button.onclick = () => {
                const form = document.getElementById("relationship-form");
                form.elements.namedItem("id").value = row.id;
                form.elements.namedItem("characterA").value = row.characterA;
                form.elements.namedItem("characterB").value = row.characterB;
                form.elements.namedItem("relationshipType").value = row.relationshipType;
                form.elements.namedItem("strength").value = row.strength;
            };
            return button;
        }}
    ], relationships);
}

function bindRelationshipForm() {
    document.getElementById("relationship-form").addEventListener("submit", async event => {
        event.preventDefault();
        const json = formJson(event.target);
        const id = json.id;
        delete json.id;
        await api(id ? `/api/relationships/${id}` : "/api/relationships", {
            method: id ? "PUT" : "POST",
            body: JSON.stringify(json)
        });
        event.target.reset();
        await loadRelationships();
    });
}

async function loadMods() {
    const toggle = document.getElementById("mods-include-inactive");
    const includeInactive = toggle ? toggle.checked : false;
    const mods = await api(`/api/mods?includeInactive=${includeInactive}`);
    const count = document.getElementById("mods-count");
    if (count) count.textContent = `${mods.length} mod(s) shown${includeInactive ? " (including historical/inactive)" : " (active only)"}`;
    renderTable("mods", [
        { label: "Name", key: "name" },
        { label: "Unique ID", key: "uniqueId" },
        { label: "Version", key: "version" },
        { label: "Author", key: "author" },
        { label: "Active", render: row => row.isActive ? "yes" : "no" },
        { label: "Last Scan", key: "lastScanTime" },
        { label: "Characters", render: row => row.characters.join(", ") }
    ], mods);
}

async function loadModStatus() {
    const status = await api("/api/mods/status");
    document.getElementById("mod-scan-status").innerHTML = `
        <div class="metric"><span>Last Scan</span><strong>${status.lastScanTime ?? "Never"}</strong></div>
        <div class="metric"><span>Last Trigger</span><strong>${status.lastTriggerSource ?? "None"}</strong></div>
        <div class="metric"><span>Active Mods</span><strong>${status.activeMods}</strong></div>
        <div class="metric"><span>Active Characters</span><strong>${status.activeCharacters}</strong></div>
        <div class="metric"><span>Inactive Characters</span><strong>${status.inactiveCharacters}</strong></div>
        <div class="metric"><span>Vanilla Characters</span><strong>${status.vanillaCharacters}</strong></div>
        <div class="metric"><span>Modded Characters</span><strong>${status.moddedCharacters}</strong></div>
        <div class="metric"><span>Canonical Profiles</span><strong>${status.mergedCanonicalCharacters}</strong></div>
        <div class="metric"><span>Conflicts</span><strong>${status.conflictsFound}</strong></div>`;
    document.getElementById("latest-scan-summary").textContent = JSON.stringify(status.latestScanSummary ?? {}, null, 2);
    renderTable("scan-history", [
        { label: "Started", key: "startedAt" },
        { label: "Trigger", key: "triggerSource" },
        { label: "Success", render: row => row.success ? "yes" : "no" },
        { label: "Mods", key: "modsScanned" },
        { label: "Found", key: "charactersFound" },
        { label: "Added", key: "charactersAdded" },
        { label: "Updated", key: "charactersUpdated" },
        { label: "Reactivated", key: "charactersReactivated" },
        { label: "Inactive", key: "charactersMarkedInactive" },
        { label: "Errors", key: "errorMessage" }
    ], status.recentScanHistory);
}

function bindModScan() {
    document.getElementById("scan-mods").addEventListener("click", async () => {
        const button = document.getElementById("scan-mods");
        const result = document.getElementById("scan-result");
        button.disabled = true;
        button.textContent = "Scanning...";
        result.className = "status";
        result.textContent = "Game file scan request starting...";
        console.log("scan request started");
        try {
            const started = await apiWithResponse("/api/mods/scan", { method: "POST" });
            console.log("scan response status code", started.response.status);
            console.log("scan response body", started.body);

            const scanRunId = started.body?.scanRunId;
            if (!scanRunId) throw new Error("Scan did not return a scanRunId.");

            result.textContent = `Scanning game files and installed mods... run ${scanRunId}`;
            const finalStatus = await pollModScanStatus(scanRunId, result);
            const summary = finalStatus.summary;
            if (!summary) throw new Error(finalStatus.message || "Scan finished without a summary.");

            result.className = summary.success ? "status success" : "status error";
            result.textContent = summary.success
                ? formatScanSuccess(summary)
                : formatScanErrors(summary.errors);

            console.log("refresh started");
            await refreshAfterScan();
            console.log("refresh completed");
        } catch (error) {
            console.log("scan failed", error);
            result.className = "status error";
            result.textContent = error.message;
        } finally {
            button.disabled = false;
            button.textContent = "Scan Game Files";
        }
    });
}

async function pollModScanStatus(scanRunId, result) {
    const started = Date.now();
    const maxWaitMs = 15 * 60 * 1000;

    while (Date.now() - started < maxWaitMs) {
        await delay(1000);
        const statusResponse = await apiWithResponse(`/api/mods/scan/status/${encodeURIComponent(scanRunId)}`);
        console.log("scan status response status code", statusResponse.response.status);
        console.log("scan status response body", statusResponse.body);

        const status = statusResponse.body;
        const elapsed = Math.round((Date.now() - started) / 1000);
        const phase = status.phase ? `${status.phase}: ` : "";
        const metrics = ` Files ${status.filesInspected ?? status.lastPhase?.filesInspected ?? 0}, characters ${status.charactersFound ?? status.lastPhase?.charactersFound ?? 0}, dialogue files ${status.dialogueFilesFound ?? status.lastPhase?.dialogueFilesFound ?? 0}.`;
        result.textContent = `${phase}${status.message || "Scanning..."} ${elapsed}s elapsed.${metrics}`;

        if (status.state === "Completed" || status.state === "Failed")
            return status;
    }

    throw new Error("Scan is still running after 15 minutes. Refresh the page and check Recent Scan History.");
}

async function refreshAfterScan() {
    const tasks = [];
    if (document.getElementById("mod-scan-status")) tasks.push(loadModStatus());
    if (document.getElementById("mods")) tasks.push(loadMods());
    if (document.getElementById("metrics")) tasks.push(loadDashboard());
    if (document.getElementById("characters")) tasks.push(loadCharacters());
    await Promise.all(tasks);
}

function delay(milliseconds) {
    return new Promise(resolve => setTimeout(resolve, milliseconds));
}

function formatScanSuccess(summary) {
    const warningCount = summary.errors ? summary.errors.length : 0;
    const warningText = warningCount > 0 ? ` ${warningCount} warning(s) were recorded in scan history.` : "";
    return `Scan complete. ${summary.modsScanned} mods scanned, ${summary.vanillaCharactersFound ?? 0} vanilla characters, ${summary.moddedCharactersFound ?? 0} modded characters, ${summary.mergedCanonicalCharacters ?? 0} canonical profiles.${warningText}`;
}

function formatScanErrors(errors) {
    if (!errors || errors.length === 0) return "Scan completed with errors.";
    const shown = errors.slice(0, 5).join(" | ");
    const remaining = errors.length > 5 ? ` (${errors.length - 5} more in scan history)` : "";
    return `Scan completed with ${errors.length} error(s): ${shown}${remaining}`;
}

async function loadConflicts() {
    const conflicts = await api("/api/conflicts");
    renderTable("conflicts", [
        { label: "Character", key: "characterName" },
        { label: "Field", key: "fieldName" },
        { label: "Mod Value", key: "modValue" },
        { label: "Override Value", key: "overrideValue" },
        { label: "Reviewed", render: row => row.isReviewed ? "yes" : "no" },
        { label: "Action", render: row => {
            const button = document.createElement("button");
            button.className = "secondary";
            button.textContent = "Mark Reviewed";
            button.disabled = row.isReviewed;
            button.onclick = async () => {
                await api(`/api/conflicts/${row.id}/reviewed`, { method: "POST" });
                await loadConflicts();
            };
            return button;
        }}
    ], conflicts);
}

async function loadMergeReview() {
    const groups = await api("/api/merge-review");
    const container = document.getElementById("merge-groups");
    const summary = document.getElementById("merge-summary");
    if (summary) {
        const dupRows = groups.reduce((total, group) => total + group.count, 0);
        summary.textContent = groups.length === 0
            ? "No duplicate character names found."
            : `${groups.length} duplicated name(s) across ${dupRows} character rows.`;
    }
    if (!container) return;

    if (groups.length === 0) {
        container.innerHTML = `<div class="empty-state"><div class="empty-icon">OK</div><p>No duplicate character names. Nothing to merge.</p></div>`;
        return;
    }

    container.innerHTML = "";
    for (const group of groups) {
        container.appendChild(renderMergeGroup(group));
    }
}

function renderMergeGroup(group) {
    const card = document.createElement("section");
    card.className = "card";

    const rows = group.characters.map(c => `
        <label class="merge-keep">
            <input type="radio" name="keep-${escapeHtml(group.name)}" value="${c.id}" ${c.isActive ? "checked" : ""}>
            <span>#${c.id} — ${escapeHtml(c.sourceModName || c.sourceModId || "vanilla/custom")}
            <small>canonical ${c.canonicalCharacterId ?? "none"}${c.isActive ? "" : " · inactive"}</small></span>
        </label>`).join("");

    card.innerHTML = `
        <div class="card-header">
            <h3>${escapeHtml(group.name)}</h3>
            <span class="chip">${group.count} rows</span>
        </div>
        <p>Choose which row to keep, then merge. The others are removed and their lore/sources are
           consolidated under the kept character.</p>
        <div class="merge-keep-list">${rows}</div>
        <div class="action-row">
            <button class="merge-button">Merge into selected</button>
            <span class="status merge-status"></span>
        </div>`;

    const button = card.querySelector(".merge-button");
    const status = card.querySelector(".merge-status");
    button.addEventListener("click", async () => {
        const chosen = card.querySelector(`input[name="keep-${cssEscape(group.name)}"]:checked`);
        if (!chosen) { status.className = "status error"; status.textContent = "Select a row to keep."; return; }
        if (!confirm(`Merge ${group.count} "${group.name}" rows into character #${chosen.value}? The other ${group.count - 1} row(s) will be removed.`)) return;
        status.className = "status";
        status.textContent = "Merging...";
        try {
            const result = await api("/api/merge-review/merge", {
                method: "POST",
                body: JSON.stringify({ name: group.name, primaryCharacterId: Number(chosen.value) })
            });
            status.className = "status success";
            status.textContent = `Merged ${result.merged} row(s).`;
            await loadMergeReview();
        } catch (error) {
            status.className = "status error";
            status.textContent = error.message;
        }
    });

    return card;
}

function cssEscape(value) {
    return String(value).replace(/["\\]/g, "\\$&");
}

function bindDialogueTestForm() {
    document.getElementById("dialogue-test-form").addEventListener("submit", async event => {
        event.preventDefault();
        const request = formJson(event.target);
        request.playerProfileId = request.playerProfileId ? Number(request.playerProfileId) : null;
        console.log("Dialogue test request", request);
        const saveContextOutput = document.getElementById("save-context-output");
        const promptOutput = document.getElementById("prompt-output");
        const dialogueOutput = document.getElementById("dialogue-output");
        const qualityOutput = document.getElementById("quality-output");
        promptOutput.textContent = "";
        dialogueOutput.textContent = "Generating...";
        if (qualityOutput) qualityOutput.textContent = "";
        document.getElementById("save-context-output").textContent = JSON.stringify({
            season: request.season,
            weather: request.weather,
            location: request.location,
            friendshipLevel: request.friendshipLevel,
            relationshipContext: request.relationshipContext || "Unknown"
        }, null, 2);
        try {
            const response = await api("/api/dialogue/test", { method: "POST", body: JSON.stringify(request) });
            console.log("Dialogue test response", response);
            saveContextOutput.textContent = JSON.stringify(response.saveContext ?? {
                season: request.season,
                weather: request.weather,
                location: request.location,
                friendshipLevel: request.friendshipLevel,
                relationshipContext: request.relationshipContext || "Unknown"
            }, null, 2);
            promptOutput.textContent = response.promptUsed ?? response.prompt ?? response.promptText ?? "";

            const returnedDialogue = response.returnedDialogue
                ?? response.generatedText
                ?? response.dialogue?.dialogue
                ?? response.dialogue
                ?? response.response
                ?? response.text
                ?? "";
            dialogueOutput.textContent = response.error
                ? `${returnedDialogue ? `${returnedDialogue}\n\n` : ""}Error: ${response.error}`
                : (typeof returnedDialogue === "string" ? returnedDialogue : JSON.stringify(returnedDialogue, null, 2));
            if (qualityOutput) renderQualityScores(qualityOutput, response.qualityScores);
            await showPlayerLoreUsed(response.historyId);
        } catch (error) {
            console.log("Dialogue test response", { error: error.message });
            dialogueOutput.textContent = `Error: ${error.message}`;
            if (qualityOutput) qualityOutput.innerHTML = "";
        }
    });
}

async function loadDialogueTestProfiles() {
    const select = document.getElementById("dialogue-profile");
    if (!select) return;
    try {
        const profiles = await api("/api/player-profiles");
        select.innerHTML = `<option value="">None / auto (active profile)</option>`;
        for (const p of profiles) {
            const opt = document.createElement("option");
            opt.value = p.id;
            opt.textContent = `${p.profileName} (${p.farmerName})${p.isActive ? " ★" : ""}`;
            select.appendChild(opt);
        }
    } catch (error) {
        // Player profiles are optional; leave the dropdown with just "None".
    }
}

async function showPlayerLoreUsed(historyId) {
    const details = document.getElementById("player-lore-details");
    const output = document.getElementById("player-lore-output");
    if (!details || !output || !historyId) { if (details) details.hidden = true; return; }
    try {
        const data = await api(`/api/dialogue/explain/${historyId}`);
        const t = data.trace;
        if (!t || !t.playerProfileUsed) { details.hidden = true; return; }
        details.hidden = false;
        output.innerHTML = renderPlayerLoreSections(t);
    } catch (error) {
        details.hidden = true;
    }
}

function renderPlayerLoreSections(t) {
    const profile = t.playerProfileUsed;
    if (!profile) return "<p>No player profile used.</p>";
    const fields = [
        ["Profile Name", profile.profileName],
        ["Description", profile.description],
        ["Backstory", profile.backstory],
        ["Personality", profile.personality],
        ["Roleplay Style", profile.roleplayStyle],
        ["Preferred Dialogue Tone", profile.preferredTone],
        ["Important History", profile.importantHistory],
        ["Current Goals", profile.currentGoals],
        ["Relationship Notes", profile.relationshipNotes],
        ["Custom Lore", profile.customLore]
    ].filter(([, value]) => value);
    const rels = (t.playerRelationshipNotesUsed || []).map(r =>
        `<li><strong>${escapeHtml(r.relationshipType)}</strong> (${escapeHtml(r.relationshipStrength)}/100): ${escapeHtml(r.relationshipDescription || "")}${r.customNotes ? ` — ${escapeHtml(r.customNotes)}` : ""}</li>`).join("");
    const mems = (t.playerMemoriesUsed || []).map(m =>
        `<li>[${escapeHtml(m.importance)}]${m.canonicalName ? ` (${escapeHtml(m.canonicalName)})` : ""} ${escapeHtml(m.memoryText)}</li>`).join("");
    return `
        <p><strong>Active Player Profile:</strong> ${escapeHtml(profile.profileName)} - farmer ${escapeHtml(profile.farmerName)}, farm ${escapeHtml(profile.farmName)}</p>
        <p><strong>Profile Match Method:</strong> ${escapeHtml(t.playerProfileMatchMethod || "none")}</p>
        ${t.saveFileLinkUsed ? `<p><strong>Save File Link Used:</strong> ${escapeHtml(t.saveFileLinkUsed)}</p>` : ""}
        <p><strong>Profile Fields Used in Prompt:</strong></p>${fields.length ? `<dl class="definition-list">${fields.map(([label, value]) => `<dt>${escapeHtml(label)}</dt><dd>${escapeHtml(value)}</dd>`).join("")}</dl>` : "<p>None.</p>"}
        <p><strong>Player Relationship Notes Used:</strong></p>${rels ? `<ul>${rels}</ul>` : "<p>None.</p>"}
        <p><strong>Player Memories Used:</strong></p>${mems ? `<ul>${mems}</ul>` : "<p>None.</p>"}`;
    return `
        <p><strong>Player Profile Used:</strong> ${escapeHtml(profile.profileName)} — farmer ${escapeHtml(profile.farmerName)}, farm ${escapeHtml(profile.farmName)}
           ${profile.preferredTone ? `· tone: ${escapeHtml(profile.preferredTone)}` : ""}</p>
        ${t.saveFileLinkUsed ? `<p><strong>Save File Link Used:</strong> ${escapeHtml(t.saveFileLinkUsed)}</p>` : ""}
        <p><strong>Player Relationship Notes Used:</strong></p>${rels ? `<ul>${rels}</ul>` : "<p>None.</p>"}
        <p><strong>Player Memories Used:</strong></p>${mems ? `<ul>${mems}</ul>` : "<p>None.</p>"}`;
}

function renderQualityScores(container, scores) {
    if (!scores) {
        container.innerHTML = `<div class="empty-state"><div class="empty-icon">QS</div><p>No quality scores returned.</p></div>`;
        return;
    }
    const items = [
        ["Character", scores.characterConsistency ?? scores.characterConsistencyScore],
        ["Context", scores.contextRelevance ?? scores.contextRelevanceScore],
        ["Relationship", scores.relationshipRelevance ?? scores.relationshipRelevanceScore],
        ["Diversity", scores.diversity ?? scores.diversityScore],
        ["Repetition Risk", scores.repetitionRisk ?? scores.repetitionRiskScore]
    ];
    container.innerHTML = items.map(([label, value]) => `
        <div class="score-pill">
            <span>${label}</span>
            <strong>${value ?? 0}</strong>
        </div>`).join("");
}

function escapeHtml(value) {
    return String(value ?? "").replace(/[&<>"']/g, ch =>
        ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[ch]));
}

async function loadDialogueExplanationPage() {
    const history = await api("/api/dialogue/history");
    renderTable("explain-history", [
        { label: "When", key: "createdDate" },
        { label: "Character", key: "characterName" },
        { label: "Topic", key: "topic" },
        { label: "Dialogue", key: "dialogueText" },
        { label: "", render: row => {
            const button = document.createElement("button");
            button.className = "secondary";
            button.textContent = "Explain";
            button.onclick = () => loadExplanation(row.id);
            return button;
        }}
    ], history);

    // Support deep links: /DialogueExplanation?id=123
    const id = new URLSearchParams(window.location.search).get("id");
    if (id) await loadExplanation(Number(id));
}

async function loadExplanation(id) {
    const detail = document.getElementById("explain-detail");
    const title = document.getElementById("explain-detail-title");
    detail.innerHTML = `<p class="status">Loading…</p>`;

    let data;
    try {
        data = await api(`/api/dialogue/explain/${id}`);
    } catch (error) {
        detail.innerHTML = `<p class="status error">${escapeHtml(error.message)}</p>`;
        return;
    }

    const line = data.generatedDialogue;
    const t = data.trace;
    title.textContent = line ? `Explanation — ${line.characterName}` : "Explanation";

    const sources = (t.dialogueSourcesUsed || []).map(s => {
        const roleTag = s.isVoiceOnly
            ? `<span style="color:#999;font-size:0.8em">[VOICE]</span>`
            : `<span style="color:#4a9;font-size:0.8em">[SCENE]</span>`;
        const scoreInfo = s.score !== undefined
            ? `<br><small style="color:#888">score: ${s.score} (scene: ${s.sceneScore}) — ${escapeHtml(s.breakdown || "")}</small>`
            : "";
        return `<li><strong>${escapeHtml(s.key || "(no key)")}</strong> ${roleTag}
            — mod: ${escapeHtml(s.mod || "unknown")}
            <br><small>file: ${escapeHtml(s.file || "n/a")}</small>
            ${s.text ? `<br><small>${escapeHtml(s.text)}</small>` : ""}
            ${scoreInfo}</li>`;
    }).join("");

    const memories = (t.memoriesUsed || []).map(m =>
        `<li>[importance ${escapeHtml(m.importance)}] ${escapeHtml(m.memoryText)}</li>`).join("");

    const relationships = (t.relationshipsUsed || []).map(r =>
        `<li>${escapeHtml(r.relationshipType)} (strength ${escapeHtml(r.strength)}) — characters ${escapeHtml(r.characterA)} &harr; ${escapeHtml(r.characterB)}</li>`).join("");

    const overrides = (t.userOverridesUsed || []).map(o =>
        `<li><strong>${escapeHtml(o.fieldName)}</strong> = ${escapeHtml(o.overrideValue)} <small>(${escapeHtml(o.overrideType)})</small>${o.notes ? ` — ${escapeHtml(o.notes)}` : ""}</li>`).join("");

    const sourceMods = (t.sourceModsUsed || []).map(m =>
        `<li>${escapeHtml(m.mod)} <small>(${escapeHtml(m.type)}, priority ${escapeHtml(m.priority)})</small></li>`).join("");
    const promptPreview = (t.promptText || "").slice(0, 1200);

    detail.innerHTML = `
        <div class="workflow">
            ${["Sources", "Context Builder", "Prompt Builder", "OpenAI", "Generated Dialogue"].map(step => `<div class="workflow-step">${step}</div>`).join("")}
        </div>
        <section class="card">
            <h4>Identity Debug</h4>
            <dl class="definition-list">
                <dt>Request Source</dt><dd>${escapeHtml(t.requestSource || "(unknown)")}</dd>
                <dt>Intercepted NPC</dt><dd>${escapeHtml(t.interceptedNpcName || "")}</dd>
                <dt>CharacterName</dt><dd>${escapeHtml(t.characterName || line?.characterName || "")}</dd>
                <dt>Resolved Character</dt><dd>${escapeHtml(t.resolvedCharacterName || "")}</dd>
                <dt>Internal Location</dt><dd>${escapeHtml(t.internalLocation || "")}</dd>
                <dt>Display Location</dt><dd>${escapeHtml(t.displayLocation || t.location || "")}</dd>
            </dl>
            <h4>Character Context Used</h4>
            <pre>${escapeHtml(JSON.stringify({ characterName: t.characterName, resolvedCharacterName: t.resolvedCharacterName, sourceMods: t.sourceModsUsed || [], dialogueSources: t.dialogueSourcesUsed || [] }, null, 2))}</pre>
            <h4>Prompt Preview</h4>
            <pre>${escapeHtml(promptPreview)}${(t.promptText || "").length > promptPreview.length ? "\n..." : ""}</pre>
        </section>
        <section class="card">
            <h4>Generated Dialogue</h4>
            <blockquote>${line ? escapeHtml(line.dialogueText) : "(line not found)"}</blockquote>
            ${line ? `<p><small>Topic: ${escapeHtml(line.topic)} | Emotion: ${escapeHtml(line.emotion)}</small></p>` : ""}
            <p>This line used prompt <code>${escapeHtml(t.promptVersion)}</code> and model <code>${escapeHtml(t.modelUsed)}</code> at ${escapeHtml(t.generatedAt)}.</p>
        </section>
        <h4>Generated Dialogue</h4>
        <blockquote>${line ? escapeHtml(line.dialogueText) : "(line not found)"}</blockquote>
        ${line ? `<p><small>Topic: ${escapeHtml(line.topic)} · Emotion: ${escapeHtml(line.emotion)}</small></p>` : ""}

        <h4>Why It Was Generated</h4>
        <p>This line was produced from the inputs below using prompt <code>${escapeHtml(t.promptVersion)}</code>
           and model <code>${escapeHtml(t.modelUsed)}</code> at ${escapeHtml(t.generatedAt)}.</p>

        <h4>Source Dialogue Used</h4>
        ${sources ? `<ul>${sources}</ul>` : "<p>None.</p>"}

        <h4>Memories Used</h4>
        ${memories ? `<ul>${memories}</ul>` : "<p>None.</p>"}

        <h4>Relationships Used</h4>
        ${relationships ? `<ul>${relationships}</ul>` : "<p>None.</p>"}

        <h4>Save Context Used</h4>
        <p><small><em>Source: ${t.requestSource && t.requestSource.includes('SMAPI') ? '<strong>SMAPI (live game state)</strong>' : 'Dashboard defaults'}</em></small></p>
        ${t.saveContext ? `<dl class="definition-list">
            <dt>Player</dt><dd>${escapeHtml(t.saveContext.playerName ?? "Unknown")}</dd>
            <dt>Farm</dt><dd>${escapeHtml(t.saveContext.farmName ?? "Unknown")}</dd>
            <dt>Save File</dt><dd>${escapeHtml(t.saveContext.saveFileName ?? "(none)")}</dd>
            <dt>Spouse</dt><dd>${escapeHtml(t.saveContext.spouse ?? "None")}</dd>
            <dt>Dating Status</dt><dd>${escapeHtml(t.saveContext.datingStatus ?? "Unknown")}</dd>
            <dt>Relationship State</dt><dd>${escapeHtml(t.saveContext.relationshipState ?? "Unknown")}</dd>
            <dt>Friendship Hearts</dt><dd>${escapeHtml(String(t.saveContext.friendshipHearts ?? 0))}</dd>
            <dt>Has Met NPC</dt><dd>${t.saveContext.hasMetNpc ? "Yes" : "No"}</dd>
            <dt>Season / Day / Year</dt><dd>${escapeHtml(t.saveContext.season ?? "?")} ${escapeHtml(String(t.saveContext.day ?? 0))}, Year ${escapeHtml(String(t.saveContext.year ?? 0))}</dd>
            <dt>Community State</dt><dd>${escapeHtml(t.saveContext.communityState ?? "Unknown")}</dd>
        </dl>` : "<p>No save context stored.</p>"}
        <details><summary>Raw JSON</summary><pre>${escapeHtml(JSON.stringify(t.saveContext ?? {}, null, 2))}</pre></details>

        <h4>Player Lore Used</h4>
        ${renderPlayerLoreSections(t)}

        <h4>User Overrides Used</h4>
        ${overrides ? `<ul>${overrides}</ul>` : "<p>None.</p>"}

        <h4>Source Mods Used</h4>
        ${sourceMods ? `<ul>${sourceMods}</ul>` : "<p>None.</p>"}

        <h4>Prompt Used</h4>
        <pre>${escapeHtml(t.promptText || "")}</pre>

        <h4>Model Used</h4>
        <p><code>${escapeHtml(t.modelUsed)}</code></p>`;
    detail.insertAdjacentHTML("beforeend", `
        <section class="card">
            <h4>Trace Cards</h4>
            <details open><summary>Save Context (${t.requestSource && t.requestSource.includes('SMAPI') ? 'SMAPI live state' : 'dashboard defaults'})</summary><pre>${escapeHtml(JSON.stringify(t.saveContext ?? {}, null, 2))}</pre></details>
            <details><summary>Dialogue Sources</summary>${sources ? `<ul>${sources}</ul>` : "<p>None.</p>"}</details>
            <details><summary>Memories</summary>${memories ? `<ul>${memories}</ul>` : "<p>None.</p>"}</details>
            <details><summary>Relationships</summary>${relationships ? `<ul>${relationships}</ul>` : "<p>None.</p>"}</details>
            <details><summary>Prompt</summary><pre>${escapeHtml(t.promptText || "")}</pre></details>
        </section>`);
}

let simulationScenarios = [];

async function loadGameSimulationPage() {
    const [characters, scenarios, profiles] = await Promise.all([
        api("/api/characters"),
        api("/api/scenarios"),
        api("/api/player-profiles").catch(() => [])
    ]);
    simulationScenarios = scenarios;

    const charSelect = document.getElementById("simulate-character");
    charSelect.innerHTML = "";
    for (const c of characters) {
        const opt = document.createElement("option");
        opt.value = c.name;
        opt.textContent = c.name;
        charSelect.appendChild(opt);
    }

    const profileSelect = document.getElementById("scenario-profile");
    if (profileSelect) {
        profileSelect.innerHTML = `<option value="">None / auto</option>`;
        for (const p of profiles) {
            const opt = document.createElement("option");
            opt.value = p.id;
            opt.textContent = `${p.profileName} (${p.farmerName})`;
            profileSelect.appendChild(opt);
        }
    }

    fillScenarioSelects();
    bindScenarioEditor();
    bindSimulateForm();
    if (scenarios.length > 0) loadScenarioIntoEditor(scenarios[0].id);
}

function fillScenarioSelects() {
    const simSelect = document.getElementById("simulate-scenario");
    const picker = document.getElementById("scenario-picker");
    for (const select of [simSelect, picker]) {
        const previous = select.value;
        select.innerHTML = "";
        for (const s of simulationScenarios) {
            const opt = document.createElement("option");
            opt.value = s.id;
            opt.textContent = s.name + (s.isBuiltIn ? " (built-in)" : "");
            select.appendChild(opt);
        }
        if (previous) select.value = previous;
    }
}

function loadScenarioIntoEditor(id) {
    const scenario = simulationScenarios.find(s => String(s.id) === String(id));
    if (!scenario) return;
    const form = document.getElementById("scenario-form");
    form.id.value = scenario.id;
    form.name.value = scenario.name;
    form.playerName.value = scenario.playerName;
    form.farmName.value = scenario.farmName;
    form.year.value = scenario.year;
    form.season.value = scenario.season;
    form.weather.value = scenario.weather;
    form.location.value = scenario.location;
    form.friendshipHearts.value = scenario.friendshipHearts;
    form.relationshipState.value = scenario.relationshipState;
    form.seenEvents.value = scenario.seenEvents;
    form.completedQuests.value = scenario.completedQuests;
    form.communityCenterState.value = scenario.communityCenterState;
    if (form.playerProfileId) form.playerProfileId.value = scenario.playerProfileId ?? "";
    document.getElementById("scenario-picker").value = scenario.id;
    updateSimulationCards(scenario);
}

function updateSimulationCards(scenario) {
    const player = document.getElementById("sim-player-card");
    const farm = document.getElementById("sim-farm-card");
    const relationship = document.getElementById("sim-relationship-card");
    if (!player || !farm || !relationship || !scenario) return;
    player.innerHTML = `<strong>${escapeHtml(scenario.playerName)}</strong><br><span class="chip">${escapeHtml(scenario.name)}</span>`;
    farm.innerHTML = `<strong>${escapeHtml(scenario.farmName)}</strong><br>Year ${escapeHtml(scenario.year)} | ${escapeHtml(scenario.season)} | ${escapeHtml(scenario.weather)}<br><span class="chip">${escapeHtml(scenario.location)}</span>`;
    relationship.innerHTML = `<strong>${escapeHtml(scenario.relationshipState)}</strong><br>${escapeHtml(scenario.friendshipHearts)} hearts<br><small>${escapeHtml(scenario.communityCenterState)}</small>`;
}

function bindScenarioEditor() {
    document.getElementById("scenario-picker").addEventListener("change", e => loadScenarioIntoEditor(e.target.value));
    document.getElementById("simulate-scenario").addEventListener("change", e => {
        const scenario = simulationScenarios.find(s => String(s.id) === String(e.target.value));
        updateSimulationCards(scenario);
    });

    document.getElementById("scenario-new").addEventListener("click", () => {
        const form = document.getElementById("scenario-form");
        form.reset();
        form.id.value = "";
    });

    document.getElementById("scenario-form").addEventListener("submit", async event => {
        event.preventDefault();
        const status = document.getElementById("scenario-status");
        const body = formJson(event.target);
        const id = body.id;
        delete body.id;
        body.playerProfileId = body.playerProfileId ? Number(body.playerProfileId) : null;
        try {
            if (id) await api(`/api/scenarios/${id}`, { method: "PUT", body: JSON.stringify(body) });
            else await api("/api/scenarios", { method: "POST", body: JSON.stringify(body) });
            simulationScenarios = await api("/api/scenarios");
            fillScenarioSelects();
            status.className = "status success";
            status.textContent = "Scenario saved.";
        } catch (error) {
            status.className = "status error";
            status.textContent = error.message;
        }
    });

    document.getElementById("scenario-delete").addEventListener("click", async () => {
        const id = document.getElementById("scenario-form").id.value;
        if (!id) return;
        if (!confirm("Delete this scenario?")) return;
        const status = document.getElementById("scenario-status");
        try {
            await api(`/api/scenarios/${id}`, { method: "DELETE" });
            simulationScenarios = await api("/api/scenarios");
            fillScenarioSelects();
            document.getElementById("scenario-form").reset();
            document.getElementById("scenario-form").id.value = "";
            status.className = "status success";
            status.textContent = "Scenario deleted.";
        } catch (error) {
            status.className = "status error";
            status.textContent = error.message;
        }
    });
}

function bindSimulateForm() {
    document.getElementById("simulate-form").addEventListener("submit", async event => {
        event.preventDefault();
        const status = document.getElementById("simulate-status");
        status.className = "status";
        status.textContent = "Running simulation…";
        const request = {
            scenarioId: Number(document.getElementById("simulate-scenario").value),
            characterName: document.getElementById("simulate-character").value,
            topic: event.target.topic.value
        };
        try {
            const data = await api("/api/simulate", { method: "POST", body: JSON.stringify(request) });
            renderSimulationReport(data);
            status.className = "status success";
            status.textContent = "Done.";
        } catch (error) {
            status.className = "status error";
            status.textContent = error.message;
        }
    });
}

function renderSimulationReport(data) {
    const report = data.report;
    const explanation = data.explanation;
    const panel = document.getElementById("simulation-report");
    const body = document.getElementById("report-body");
    panel.hidden = false;

    const validation = (report.validation || []).map(v =>
        `<li class="${v.passed ? "rule-pass" : "rule-fail"}">${v.passed ? "✓" : "✗"} ${escapeHtml(v.name)} — ${escapeHtml(v.detail || "")}</li>`).join("");

    const sources = explanation ? (explanation.dialogueSourcesUsed || []).map(s => {
        const roleTag = s.isVoiceOnly
            ? `<span style="color:#999;font-size:0.8em">[VOICE]</span>`
            : `<span style="color:#4a9;font-size:0.8em">[SCENE]</span>`;
        return `<li><strong>${escapeHtml(s.key || "(no key)")}</strong> ${roleTag} — ${escapeHtml(s.mod || "unknown")} <small>${escapeHtml(s.file || "")}</small></li>`;
    }).join("") : "";
    const memories = explanation ? (explanation.memoriesUsed || []).map(m =>
        `<li>${escapeHtml(m.memoryText)}</li>`).join("") : "";

    const scenario = report.scenario;
    body.innerHTML = `
        <p><strong>Status:</strong> <span class="conn-${report.allValidationPassed ? "ok" : "bad"}">${report.allValidationPassed ? "All checks passed" : "Some checks failed"}</span></p>

        <h4>Character &amp; Scenario</h4>
        <p>Character: <strong>${escapeHtml(report.characterName)}</strong>
           ${report.canonicalName ? `(canonical: ${escapeHtml(report.canonicalName)})` : ""}<br>
           Scenario: <strong>${escapeHtml(scenario ? scenario.name : "n/a")}</strong> · Topic: ${escapeHtml(report.topic)}</p>
        <pre>${escapeHtml(JSON.stringify(report.saveContext ?? {}, null, 2))}</pre>

        <h4>Validation Status</h4>
        <ul class="rule-list">${validation}</ul>

        <h4>Generated Dialogue</h4>
        <blockquote>${report.dialogueText ? escapeHtml(report.dialogueText) : "(none generated)"}</blockquote>
        ${report.error ? `<p class="status error">${escapeHtml(report.error)}</p>` : ""}

        <h4>Explanation</h4>
        ${explanation ? `
            <p>Model: <code>${escapeHtml(explanation.modelUsed)}</code> · Prompt version: <code>${escapeHtml(explanation.promptVersion)}</code></p>
            <p><strong>Source dialogue used:</strong></p>${sources ? `<ul>${sources}</ul>` : "<p>None.</p>"}
            <p><strong>Memories used:</strong></p>${memories ? `<ul>${memories}</ul>` : "<p>None.</p>"}
            ${report.historyId ? link(`/DialogueExplanation?id=${report.historyId}`, "Open full explanation").outerHTML : ""}
        ` : "<p>No explanation trace (dialogue was not generated).</p>"}

        <h4>Dialogue Override Preview</h4>
        <p><strong>Original dialogue:</strong></p>
        <blockquote>${report.originalDialogue ? escapeHtml(report.originalDialogue) : "(no original source matched)"}</blockquote>
        <p><strong>Generated override</strong> (key: <code>${escapeHtml(report.overrideKey || "n/a")}</code>):</p>
        <blockquote>${report.overrideText ? escapeHtml(report.overrideText) : "(none)"}</blockquote>
        <p><strong>Content Patcher export preview:</strong></p>
        <pre>${report.contentPatcherPreview ? escapeHtml(report.contentPatcherPreview) : "(none)"}</pre>

        <h4>Prompt Used</h4>
        <pre>${escapeHtml(report.prompt || "")}</pre>`;
}

async function loadDialogueContextPage() {
    const select = document.getElementById("dialogue-context-character");
    const characters = await api("/api/canonical-characters");
    select.innerHTML = "";
    for (const character of characters) {
        const option = document.createElement("option");
        option.value = character.id;
        option.textContent = character.displayName;
        select.appendChild(option);
    }

    document.getElementById("dialogue-context-form").addEventListener("submit", async event => {
        event.preventDefault();
        await loadDialogueContext(select.value);
    });

    if (characters.length > 0) await loadDialogueContext(characters[0].id);
}

async function loadDialogueContext(canonicalId) {
    const context = await api(`/api/dialogue/context/${canonicalId}`);
    document.getElementById("dialogue-context-summary").textContent = JSON.stringify(context.summary ?? {}, null, 2);
    renderTable("dialogue-context-sources", [
        { label: "Source Mod", key: "sourceModId" },
        { label: "Key", key: "dialogueKey" },
        { label: "Text", key: "rawText" },
        { label: "Conditions", key: "conditions" },
        { label: "Season", key: "season" },
        { label: "Hearts", key: "heartLevel" },
        { label: "Relationship", key: "relationshipState" },
        { label: "Active", render: row => row.isActive ? "yes" : "no" }
    ], context.sources);
}

async function loadDialogueOverrides() {
    const overrides = await api("/api/dialogue/overrides");
    renderTable("dialogue-overrides", [
        { label: "ID", key: "id" },
        { label: "Canonical ID", key: "canonicalCharacterId" },
        { label: "Key", key: "dialogueKey" },
        { label: "Generated Dialogue", key: "generatedText" },
        { label: "Approved", render: row => row.isApproved ? "yes" : "no" },
        { label: "Enabled", render: row => row.isEnabled ? "yes" : "no" },
        { label: "Save Context", key: "saveContextSnapshot" },
        { label: "Actions", render: row => overrideActions(row) }
    ], overrides);
}

function overrideActions(row) {
    const wrap = document.createElement("div");
    wrap.className = "action-row";
    for (const [label, url] of [
        ["Approve", `/api/dialogue/overrides/${row.id}/approve`],
        ["Enable", `/api/dialogue/overrides/${row.id}/enable`]
    ]) {
        const button = document.createElement("button");
        button.className = "secondary";
        button.textContent = label;
        button.onclick = async () => {
            await api(url, { method: "POST" });
            await loadDialogueOverrides();
        };
        wrap.appendChild(button);
    }
    return wrap;
}

function bindDialogueExport() {
    const button = document.getElementById("export-overrides");
    if (!button) return;
    button.addEventListener("click", async () => {
        const result = document.getElementById("export-result");
        result.className = "status";
        result.textContent = "Exporting...";
        try {
            const summary = await api("/api/dialogue/export", { method: "POST" });
            result.className = "status success";
            result.textContent = `Exported ${summary.overridesExported} override(s) to ${summary.outputPath}.`;
        } catch (error) {
            result.className = "status error";
            result.textContent = error.message;
        }
    });
}

async function loadSettings() {
    const settings = await api("/api/settings");
    const form = document.getElementById("settings-form");
    form.databasePath.value = settings.databasePath;
    form.openAiApiKeyEnvironmentVariable.value = settings.openAiApiKeyEnvironmentVariable;
    form.hasOpenAiApiKey.value = settings.hasOpenAiApiKey ? "yes" : "no";

    const modelSelect = document.getElementById("settings-model-select");
    const options = (settings.availableModels || []).slice();
    if (settings.openAiModel && !options.includes(settings.openAiModel)) options.unshift(settings.openAiModel);
    modelSelect.innerHTML = "";
    for (const model of options) {
        const opt = document.createElement("option");
        opt.value = model;
        opt.textContent = model;
        if (model === settings.openAiModel) opt.selected = true;
        modelSelect.appendChild(opt);
    }

    form.gamePath.value = settings.gamePath ?? "";
    form.modsFolderPath.value = settings.modsFolderPath ?? "";
    form.enableLiveInGameDialogueGeneration.checked = settings.enableLiveInGameDialogueGeneration;
}

function bindSettingsForm() {
    document.getElementById("settings-form").addEventListener("submit", async event => {
        event.preventDefault();
        await api("/api/settings", { method: "POST", body: JSON.stringify(formJson(event.target)) });
        await loadSettings();
    });
}

function link(href, text) {
    const a = document.createElement("a");
    a.href = href;
    a.textContent = text;
    a.className = "button-link";
    return a;
}

// ---- Player Profiles --------------------------------------------------------------------------

async function loadPlayerProfilesPage() {
    bindPlayerProfileForm();
    await refreshPlayerProfilesList();
}

async function refreshPlayerProfilesList() {
    const profiles = await api("/api/player-profiles");
    const activeProfile = profiles.find(p => p.isActive);
    const warning = document.getElementById("player-profile-warning");
    if (warning) {
        warning.hidden = !!activeProfile;
        warning.textContent = activeProfile ? "" : "No active player profile selected. SMAPI dialogue will still generate using live save context only.";
    }
    renderTable("player-profiles", [
        { label: "Profile", render: row => link(`/PlayerProfile?id=${row.id}`, row.profileName) },
        { label: "Farmer", key: "farmerName" },
        { label: "Farm", key: "farmName" },
        { label: "Linked Save", render: row => row.saveFileName || "—" },
        { label: "Active", render: row => row.isActive ? "★ active" : "archived" },
        { label: "Actions", render: row => {
            const wrap = document.createElement("div");
            wrap.className = "action-row";
            const edit = document.createElement("button");
            edit.className = "secondary";
            edit.textContent = "Edit";
            edit.onclick = () => loadProfileIntoForm(row);
            const active = document.createElement("button");
            active.className = "secondary";
            active.textContent = "Set Active";
            active.onclick = async () => { await api(`/api/player-profiles/${row.id}/set-active`, { method: "POST" }); await refreshPlayerProfilesList(); };
            const archive = document.createElement("button");
            archive.className = "danger";
            archive.textContent = "Archive";
            archive.onclick = async () => { await api(`/api/player-profiles/${row.id}/archive`, { method: "POST" }); await refreshPlayerProfilesList(); };
            wrap.append(edit, active, archive);
            return wrap;
        }}
    ], profiles);
}

function loadProfileIntoForm(profile) {
    const form = document.getElementById("profile-form");
    document.getElementById("profile-form-title").textContent = `Edit: ${profile.profileName}`;
    for (const key of ["id", "profileName", "farmerName", "farmName", "saveFileName", "saveFilePath",
        "description", "backstory", "personality", "roleplayStyle", "preferredTone",
        "importantHistory", "currentGoals", "relationshipNotes", "customLore"]) {
        if (form[key]) form[key].value = profile[key] ?? "";
    }
}

function bindPlayerProfileForm() {
    document.getElementById("profile-new").addEventListener("click", () => {
        document.getElementById("profile-form").reset();
        document.getElementById("profile-form").id.value = "";
        document.getElementById("profile-form-title").textContent = "Create Player Profile";
    });

    document.getElementById("profile-form").addEventListener("submit", async event => {
        event.preventDefault();
        const status = document.getElementById("profile-status");
        const body = formJson(event.target);
        const id = body.id;
        delete body.id;
        try {
            if (id) await api(`/api/player-profiles/${id}`, { method: "PUT", body: JSON.stringify(body) });
            else await api("/api/player-profiles", { method: "POST", body: JSON.stringify(body) });
            status.className = "status success";
            status.textContent = "Profile saved.";
            await refreshPlayerProfilesList();
        } catch (error) {
            status.className = "status error";
            status.textContent = error.message;
        }
    });
}

async function loadPlayerProfileDetailPage() {
    const id = Number(new URLSearchParams(window.location.search).get("id"));
    if (!id) { document.getElementById("profile-summary").textContent = "No profile id provided."; return; }

    const [detail, characters] = await Promise.all([
        api(`/api/player-profiles/${id}`),
        api("/api/canonical-characters").catch(() => [])
    ]);

    const p = detail.profile;
    document.getElementById("profile-detail-title").textContent = `Player Profile: ${p.profileName}`;
    document.getElementById("profile-summary").innerHTML = `
        <p><strong>${escapeHtml(p.profileName)}</strong> — farmer ${escapeHtml(p.farmerName)}, farm ${escapeHtml(p.farmName)}
           ${p.isActive ? '<span class="chip">★ active</span>' : ""}</p>
        ${p.saveFileName ? `<p>Linked save: <code>${escapeHtml(p.saveFileName)}</code></p>` : ""}
        ${p.personality ? `<p><strong>Personality:</strong> ${escapeHtml(p.personality)}</p>` : ""}
        ${p.preferredTone ? `<p><strong>Preferred tone:</strong> ${escapeHtml(p.preferredTone)}</p>` : ""}`;

    fillCharacterSelect(document.getElementById("rel-character"), characters, false);
    fillCharacterSelect(document.getElementById("mem-character"), characters, true);

    renderTable("profile-relationships", [
        { label: "Character", render: row => row.canonicalName || row.canonicalCharacterId },
        { label: "Type", key: "relationshipType" },
        { label: "Strength", key: "relationshipStrength" },
        { label: "Description", key: "relationshipDescription" },
        { label: "Notes", key: "customNotes" }
    ], detail.relationships);

    renderTable("profile-memories", [
        { label: "Character", render: row => row.canonicalName || "general" },
        { label: "Importance", key: "importance" },
        { label: "Memory", key: "memoryText" }
    ], detail.memories);

    renderTable("profile-savelinks", [
        { label: "Save File", key: "saveFileName" },
        { label: "Default", render: row => row.isDefaultForSave ? "yes" : "no" },
        { label: "Last Seen", key: "lastSeen" }
    ], detail.saveLinks);

    bindProfileDetailForms(id);
}

function fillCharacterSelect(select, characters, includeBlank) {
    if (!select) return;
    select.innerHTML = includeBlank ? `<option value="">General memory</option>` : "";
    for (const c of characters) {
        const opt = document.createElement("option");
        opt.value = c.id;
        opt.textContent = c.canonicalName || c.displayName || c.name || `#${c.id}`;
        select.appendChild(opt);
    }
}

function bindProfileDetailForms(profileId) {
    document.getElementById("profile-relationship-form").addEventListener("submit", async event => {
        event.preventDefault();
        const status = document.getElementById("rel-status");
        const body = formJson(event.target);
        body.canonicalCharacterId = Number(body.canonicalCharacterId);
        try {
            await api(`/api/player-profiles/${profileId}/relationships`, { method: "POST", body: JSON.stringify(body) });
            status.className = "status success"; status.textContent = "Relationship note added.";
            await loadPlayerProfileDetailPage();
        } catch (error) { status.className = "status error"; status.textContent = error.message; }
    });

    document.getElementById("profile-memory-form").addEventListener("submit", async event => {
        event.preventDefault();
        const status = document.getElementById("mem-status");
        const body = formJson(event.target);
        body.canonicalCharacterId = body.canonicalCharacterId ? Number(body.canonicalCharacterId) : null;
        try {
            await api(`/api/player-profiles/${profileId}/memories`, { method: "POST", body: JSON.stringify(body) });
            status.className = "status success"; status.textContent = "Memory added.";
            await loadPlayerProfileDetailPage();
        } catch (error) { status.className = "status error"; status.textContent = error.message; }
    });

    document.getElementById("profile-savelink-form").addEventListener("submit", async event => {
        event.preventDefault();
        const status = document.getElementById("savelink-status");
        const body = formJson(event.target);
        try {
            await api(`/api/player-profiles/${profileId}/link-save`, { method: "POST", body: JSON.stringify(body) });
            status.className = "status success"; status.textContent = "Save linked.";
            await loadPlayerProfileDetailPage();
        } catch (error) { status.className = "status error"; status.textContent = error.message; }
    });
}

document.addEventListener("DOMContentLoaded", initShell);
