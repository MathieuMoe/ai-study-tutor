// TutorForge – chat client.
// The conversation lives only in this page (memory): nothing is stored on the server or in the browser.
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
  level: $("level"),
  language: $("language"),
};

/** @type {{role: "User" | "Assistant", content: string}[]} */
let history = [];
let controller = null;

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
  let html = DOMPurify.sanitize(marked.parse(protectedText));
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
      history.push({ role: "Assistant", content: answer });
      node.classList.add("done");
      node.querySelector("[data-copy]").addEventListener("click", (e) => copy(answer, e.currentTarget));
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
    .map((m) => `**${m.role === "User" ? t("Student") : t("Tutor")}:**\n\n${m.content.trim()}`)
    .join("\n\n---\n\n");
  const header = `# TutorForge · ${contextEl.textContent}\n\n`;
  const url = URL.createObjectURL(new Blob([header + md + "\n"], { type: "text/markdown" }));
  Object.assign(document.createElement("a"), { href: url, download: "tutor-conversation.md" }).click();
  URL.revokeObjectURL(url);
}

function newConversation() {
  controller?.abort();
  history = [];
  messagesEl.querySelectorAll(":scope > :not(#welcome)").forEach((n) => n.remove());
  welcome.hidden = false;
  showError("");
  setBusy(false);
  prompt.focus();
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

[settings.subject, settings.level, settings.language].forEach((s) => s.addEventListener("change", updateContext));
document.querySelectorAll('input[name="mode"]').forEach((r) => r.addEventListener("change", updateContext));

updateContext();
setBusy(false);
prompt.focus();
