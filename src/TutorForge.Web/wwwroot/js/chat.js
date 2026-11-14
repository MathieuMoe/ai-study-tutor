// MatGPT Tutor – chat client.
// Progress (per subject: settings, conversation, level history) is saved anonymously on the server
// through /api/progress, behind an IStudentProgressStore that can be a database in production.
import { marked } from "/lib/marked/marked.esm.js";
import DOMPurify from "/lib/dompurify/purify.es.mjs";
import renderMathInElement from "/lib/katex/contrib/auto-render.mjs";

const strings = JSON.parse(document.getElementById("i18n")?.textContent || "{}");
const t = (key, ...args) => (strings[key] ?? key).replace(/\{(\d+)\}/g, (_, i) => args[Number(i)]);

const $ = (id) => document.getElementById(id);
const messagesEl = $("messages");
const welcome = $("welcome");
const form = $("composer");
const prompt = $("prompt");
const sendBtn = $("send-btn");
const stopBtn = $("stop-btn");
const exportBtn = $("export-btn");
const errorBox = $("chat-error");
const contextEl = $("chat-context");
const settings = {
  subject: $("subject"),
  topic: $("topic"),
  level: $("level"),
  language: $("language"),
};
const levelUi = {
  card: $("level-card"),
  value: $("level-value"),
  trend: $("level-trend"),
  bar: $("level-bar"),
  barWrap: $("level-bar-wrap"),
  badge: $("level-badge"),
};

/** @type {{role: "User" | "Assistant", content: string}[]} */
let history = [];
let controller = null;
let estimatedLevel = null;
let levelHistory = [];
/** Saved progress of every subject, as returned by GET /api/progress. */
let saved = {};
let currentSubject = settings.subject.value;
const dateFormat = new Intl.DateTimeFormat(document.documentElement.lang || undefined, { dateStyle: "medium", timeStyle: "short" });

// ---------- Level tracking ----------
// The tutor appends a hidden tag [[LEVEL:n]] (0-10) in "Assess" and "Practice" modes.
const LEVEL_TAG = /\[\[LEVEL:(\d{1,2})\]\]/g;

/** Removes level tags (and a tag still being streamed) from the text shown to the student. */
const HIDDEN_TAGS = /\[\[(?:LEVEL|SEED):\d+\]\]/g;
const stripTags = (text) => text.replace(HIDDEN_TAGS, "").replace(/\[\[[A-Z:]*\d*\]?$/, "").trimEnd();

function readLevel(text) {
  const matches = [...text.matchAll(LEVEL_TAG)];
  if (!matches.length) return;
  const level = Math.max(0, Math.min(10, Number(matches.at(-1)[1])));
  if (level !== estimatedLevel) levelHistory = [...levelHistory, { level, at: new Date().toISOString() }].slice(-50);
  showLevel(level, estimatedLevel);
}

/** Shows the level card: value, bar, trend versus the previous estimate, sparkline and date. */
function showLevel(level, previous) {
  estimatedLevel = level;
  levelUi.card.hidden = false;
  levelUi.value.textContent = level;
  levelUi.bar.style.width = `${level * 10}%`;
  levelUi.barWrap.setAttribute("aria-valuenow", level);
  levelUi.trend.textContent = previous === null || previous === level ? "" : level > previous ? `▲ +${level - previous}` : `▼ ${level - previous}`;
  levelUi.trend.className = `text-xs ${level >= (previous ?? level) ? "text-teal-600 dark:text-teal-400" : "text-red-600 dark:text-red-400"}`;
  levelUi.badge.textContent = `${t("Level")} ${level}/10`;
  levelUi.badge.hidden = false;
  drawSparkline();
}

function drawSparkline() {
  const points = levelHistory.slice(-12);
  const spark = $("level-spark");
  const date = $("level-date");
  spark.toggleAttribute("hidden", points.length < 2); // SVG elements have no .hidden property
  if (points.length >= 2) {
    const step = 100 / (points.length - 1);
    spark.querySelector("polyline").setAttribute("points", points.map((p, i) => `${(i * step).toFixed(1)},${(22 - p.level * 2).toFixed(1)}`).join(" "));
  }
  const last = points.at(-1);
  date.textContent = last ? t("LastUpdate", dateFormat.format(new Date(last.at))) : "";
}

function resetLevel() {
  estimatedLevel = null;
  levelHistory = [];
  levelUi.card.hidden = true;
  levelUi.badge.hidden = true;
  levelUi.bar.style.width = "0";
}

marked.setOptions({ breaks: true, gfm: true });

// ---------- Rendering ----------

/** Markdown -> sanitized HTML, with LaTeX math rendered by KaTeX. */
function renderMarkdown(element, markdown) {
  // Protect LaTeX from the Markdown parser (underscores, asterisks...)
  const math = [];
  const protectedText = markdown.replace(/\$\$[\s\S]+?\$\$|\$[^\s$](?:[^$\n]*?[^\s$])?\$/g, (m) => {
    math.push(m);
    return `@@MATH${math.length - 1}@@`;
  });
  let html = DOMPurify.sanitize(marked.parse(stripTags(protectedText)));
  html = html.replace(/@@MATH(\d+)@@/g, (_, i) => escapeHtml(math[Number(i)]));
  element.innerHTML = html;
  renderMathInElement(element, {
    delimiters: [
      { left: "$$", right: "$$", display: true },
      { left: "$", right: "$", display: false },
    ],
    throwOnError: false,
  });
}

const escapeHtml = (s) => s.replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);

function addMessage(role, content = "") {
  welcome.hidden = true;
  const tpl = document.getElementById(role === "User" ? "tpl-user" : "tpl-assistant");
  const node = tpl.content.firstElementChild.cloneNode(true);
  const contentEl = node.querySelector("[data-content]");
  if (role === "User") contentEl.textContent = content;
  else if (content) renderMarkdown(contentEl, content);
  messagesEl.append(node);
  scrollToBottom();
  return { node, contentEl };
}

function typingIndicator(contentEl) {
  contentEl.innerHTML =
    '<span class="inline-flex gap-1 py-2" aria-label="…">' +
    '<span class="size-1.5 rounded-full bg-muted-foreground animate-bounce"></span>' +
    '<span class="size-1.5 rounded-full bg-muted-foreground animate-bounce [animation-delay:.15s]"></span>' +
    '<span class="size-1.5 rounded-full bg-muted-foreground animate-bounce [animation-delay:.3s]"></span></span>';
}

const scrollToBottom = () => messagesEl.scrollTo({ top: messagesEl.scrollHeight });

function updateContext() {
  const label = (select) => select.options[select.selectedIndex].text;
  const mode = document.querySelector('input[name="mode"]:checked');
  const modeLabel = mode?.closest("label").querySelector(".font-medium").textContent ?? "";
  contextEl.textContent = `${label(settings.subject)} · ${label(settings.level)} · ${modeLabel}`;
}

const showError = (message) => {
  errorBox.textContent = message;
  errorBox.hidden = !message;
};

const setBusy = (busy) => {
  sendBtn.hidden = busy;
  stopBtn.hidden = !busy;
  prompt.disabled = busy;
  exportBtn.disabled = busy || history.length === 0;
};

// ---------- Sending ----------

async function send(text) {
  text = text.trim();
  if (!text || controller) return;
  showError("");

  history.push({ role: "User", content: text });
  addMessage("User", text);
  prompt.value = "";
  autoResize();

  const { node, contentEl } = addMessage("Assistant");
  typingIndicator(contentEl);
  controller = new AbortController();
  setBusy(true);

  let answer = "";
  let failed = false;
  try {
    const response = await fetch("/api/tutor/stream", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        messages: history,
        subject: settings.subject.value,
        level: settings.level.value,
        mode: document.querySelector('input[name="mode"]:checked').value,
        language: settings.language.value,
        topic: settings.topic.value.trim() || null,
      }),
      signal: controller.signal,
    });

    if (!response.ok) {
      const data = await response.json().catch(() => ({}));
      throw new Error(response.status === 429 ? "rate_limited" : data.code || "ai_error");
    }

    // Read the server-sent events as they arrive
    const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
    let buffer = "";
    let lastRender = 0;
    for (;;) {
      const { value, done } = await reader.read();
      if (done) break;
      buffer += value;
      const events = buffer.split("\n\n");
      buffer = events.pop();
      for (const evt of events) {
        const type = /^event: (.*)$/m.exec(evt)?.[1] ?? "message";
        const data = /^data: (.*)$/m.exec(evt)?.[1];
        if (type === "error") throw new Error(JSON.parse(data).code || "ai_error");
        if (type === "message" && data) answer += JSON.parse(data).text;
      }
      // Re-render at most every 60 ms (smooth and cheap)
      if (answer && performance.now() - lastRender > 60) {
        renderMarkdown(contentEl, answer);
        lastRender = performance.now();
        scrollToBottom();
      }
    }
  } catch (err) {
    if (err.name !== "AbortError") {
      failed = true;
      showError(t(err.message) === err.message ? t("ai_error") : t(err.message));
    }
  } finally {
    controller = null;
    if (answer) {
      renderMarkdown(contentEl, answer);
      readLevel(answer);
      history.push({ role: "Assistant", content: answer });
      finishAssistantNode(node, answer);
      saveProgress();
    } else {
      node.remove();
      if (failed) {
        // Let the student retry: put their message back in the box
        history.pop();
        messagesEl.lastElementChild?.remove();
        prompt.value = text;
      }
    }
    setBusy(false);
    prompt.focus();
    scrollToBottom();
  }
}

async function copy(text, button) {
  try {
    await navigator.clipboard.writeText(text);
    const label = button.querySelector("span");
    const old = label.textContent;
    label.textContent = t("Copied");
    setTimeout(() => (label.textContent = old), 1500);
  } catch { /* clipboard not available */ }
}

function exportConversation() {
  const md = history
    .map((m) => `**${m.role === "User" ? t("Student") : t("Tutor")}:**\n\n${stripTags(m.content).trim()}`)
    .join("\n\n---\n\n");
  const header = `# MatGPT Tutor · ${contextEl.textContent}\n\n`;
  const url = URL.createObjectURL(new Blob([header + md + "\n"], { type: "text/markdown" }));
  Object.assign(document.createElement("a"), { href: url, download: "tutor-conversation.md" }).click();
  URL.revokeObjectURL(url);
}

function finishAssistantNode(node, answer) {
  node.classList.add("done");
  node.querySelector("[data-copy]").addEventListener("click", (e) => copy(stripTags(answer), e.currentTarget));
}

function clearChat() {
  controller?.abort();
  history = [];
  messagesEl.querySelectorAll(":scope > :not(#welcome)").forEach((n) => n.remove());
  welcome.hidden = false;
  showError("");
  setBusy(false);
}

/** New conversation in the same subject: the chat is cleared, the level and its history are kept. */
function newConversation() {
  clearChat();
  saveProgress();
  prompt.focus();
}

// ---------- Saved progress ----------

const MAX_SAVED_MESSAGES = 30;

/** Keeps the last messages, starting with a student message (what the API expects). */
function messagesToSave() {
  let messages = history.slice(-MAX_SAVED_MESSAGES);
  while (messages.length && messages[0].role !== "User") messages = messages.slice(1);
  return messages;
}

let saveTimer = null;
function saveProgress(delay = 0) {
  clearTimeout(saveTimer);
  saveTimer = setTimeout(async () => {
    const subject = currentSubject;
    const body = {
      topic: settings.topic.value.trim() || null,
      schoolLevel: settings.level.value,
      mode: document.querySelector('input[name="mode"]:checked').value,
      language: settings.language.value,
      estimatedLevel,
      messages: messagesToSave(),
    };
    try {
      const response = await fetch(`/api/progress/${subject}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      if (!response.ok) throw new Error(String(response.status));
      saved[subject] = { ...body, levelHistory: [...levelHistory], updatedAt: new Date().toISOString() };
      $("save-status").textContent = t("Saved");
    } catch (err) {
      console.warn("Progress not saved", err);
      $("save-status").textContent = t("NotSaved");
    }
  }, delay);
}

/** Restores a subject: its settings, conversation and level, or the welcome screen if it was never used. */
function showSubject(subject) {
  clearChat();
  currentSubject = subject;
  const state = saved[subject];

  settings.topic.value = state?.topic ?? "";
  if (state) {
    settings.level.value = state.schoolLevel;
    settings.language.value = state.language;
  }
  const mode = document.querySelector(`input[name="mode"][value="${state?.mode ?? "Guide"}"]`);
  if (mode) mode.checked = true;

  levelHistory = state?.levelHistory ?? [];
  if (state?.estimatedLevel != null) {
    const previous = levelHistory.length >= 2 ? levelHistory.at(-2).level : null;
    showLevel(state.estimatedLevel, previous);
  } else {
    resetLevel();
  }

  for (const m of state?.messages ?? []) {
    history.push(m);
    const { node } = addMessage(m.role, m.content);
    if (m.role === "Assistant") finishAssistantNode(node, m.content);
  }

  updateContext();
  updateAssessSubject();
  setBusy(false);
  scrollToBottom();
}

async function loadProgress() {
  try {
    const response = await fetch("/api/progress");
    if (response.status !== 200) return;
    const data = await response.json();
    saved = data.subjects ?? {};
    const last = data.lastSubject;
    if (last && settings.subject.querySelector(`option[value="${last}"]`)) settings.subject.value = last;
  } catch (err) {
    console.warn("Progress not loaded", err);
  }
}

let forgetArmed = false;
async function forgetMe(button) {
  if (!forgetArmed) {
    forgetArmed = true;
    button.textContent = t("ForgetConfirm");
    setTimeout(() => { forgetArmed = false; button.textContent = t("Forget"); }, 4000);
    return;
  }
  forgetArmed = false;
  await fetch("/api/progress", { method: "DELETE" }).catch(() => {});
  saved = {};
  resetLevel();
  showSubject(settings.subject.value);
  button.textContent = t("Forget");
  $("save-status").textContent = t("Forgotten");
}

// ---------- Events ----------

function autoResize() {
  prompt.style.height = "auto";
  prompt.style.height = `${Math.min(prompt.scrollHeight, 192)}px`;
}

form.addEventListener("submit", (e) => {
  e.preventDefault();
  send(prompt.value);
});
prompt.addEventListener("keydown", (e) => {
  if (e.key === "Enter" && !e.shiftKey && !e.isComposing) {
    e.preventDefault();
    send(prompt.value);
  }
});
prompt.addEventListener("input", autoResize);
stopBtn.addEventListener("click", () => controller?.abort());
exportBtn.addEventListener("click", exportConversation);
document.getElementById("new-btn").addEventListener("click", newConversation);

document.querySelectorAll("[data-suggestion]").forEach((btn) =>
  btn.addEventListener("click", () => {
    const mode = document.querySelector(`input[name="mode"][value="${btn.dataset.mode}"]`);
    if (mode) mode.checked = true;
    updateContext();
    send(btn.dataset.suggestion);
  })
);

// ---------- Adaptive level test ----------
const selectedText = (select) => select.options[select.selectedIndex].text;

function startAssessment() {
  if (controller) return;
  if (history.length) clearChat();
  document.querySelector('input[name="mode"][value="Assess"]').checked = true;
  updateContext();
  const topic = settings.topic.value.trim();
  const subject = selectedText(settings.subject);
  send(topic ? t("AssessStartTopic", subject, topic) : t("AssessStart", subject));
}

const updateAssessSubject = () => {
  const topic = settings.topic.value.trim();
  $("assess-subject").textContent = topic ? `${selectedText(settings.subject)} · ${topic}` : selectedText(settings.subject);
};

$("assess-btn").addEventListener("click", startAssessment);

// Changing the subject saves nothing new (each answer is already saved) and shows that subject:
// its saved conversation and level, or the welcome screen with the level test, like a fresh page.
settings.subject.addEventListener("change", () => {
  showSubject(settings.subject.value);
  // Remember it as the last subject (so a reload comes back here) if it already has progress
  if (saved[currentSubject]) saveProgress();
});
[settings.level, settings.language].forEach((s) =>
  s.addEventListener("change", () => {
    updateContext();
    if (saved[currentSubject] || history.length) saveProgress(400);
  })
);
settings.topic.addEventListener("input", () => {
  updateAssessSubject();
  if (saved[currentSubject] || history.length) saveProgress(800);
});
document.querySelectorAll('input[name="mode"]').forEach((r) =>
  r.addEventListener("change", () => {
    updateContext();
    // Choosing "Assess my level" on an empty conversation starts the test right away
    if (r.value === "Assess" && r.checked && history.length === 0) startAssessment();
    else if (saved[currentSubject] || history.length) saveProgress(400);
  })
);
$("forget-btn").addEventListener("click", (e) => forgetMe(e.currentTarget));

await loadProgress();
showSubject(settings.subject.value);
prompt.focus();
