// MatGPT Tutor – "My progress": demo data and JSON editor of the student record.
const strings = JSON.parse(document.getElementById("i18n")?.textContent || "{}");
const t = (key) => strings[key] ?? key;
const $ = (id) => document.getElementById(id);
const status = $("data-status");
const editor = $("record-json");
const language = { en: "English", fr: "French", es: "Spanish", ca: "Catalan" }[document.documentElement.lang] ?? "English";

function show(message, ok) {
  status.textContent = message;
  status.className = `text-xs ${ok ? "text-teal-600 dark:text-teal-400" : "text-red-600 dark:text-red-400"}`;
}

$("sample-btn")?.addEventListener("click", async () => {
  const response = await fetch(`/api/record/sample?language=${language}`, { method: "POST" });
  if (response.ok) location.reload();
});

$("save-json").addEventListener("click", async () => {
  let body;
  try { body = JSON.parse(editor.value); } catch { show(t("InvalidJson"), false); return; }
  const response = await fetch("/api/record", { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
  if (!response.ok) { show(t("InvalidJson"), false); return; }
  show(t("RecordSaved"), true);
  setTimeout(() => { location.hash = "data-panel"; location.reload(); }, 600);
});

$("import-json").addEventListener("change", async (e) => {
  const file = e.target.files?.[0];
  e.target.value = "";
  if (!file) return;
  try {
    const data = JSON.parse(await file.text());
    // Accepts the record alone or the export file { record, progress }
    editor.value = JSON.stringify(data.record ?? data, null, 2);
    show(t("ImportReady"), true);
  } catch {
    show(t("InvalidJson"), false);
  }
});

let armed = false;
$("delete-record").addEventListener("click", async (e) => {
  if (!armed) {
    armed = true;
    e.currentTarget.textContent = t("ConfirmDelete");
    setTimeout(() => { armed = false; e.target.textContent = t("DeleteRecord"); }, 3000);
    return;
  }
  await fetch("/api/record", { method: "DELETE" });
  location.reload();
});

// Keep the JSON panel open after saving
if (location.hash === "#data-panel") $("data-panel").open = true;
