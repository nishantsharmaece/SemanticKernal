const state = {
  conversations: [],
  reminders: [],
  notifiedReminderIds: new Set(),
  notificationPermissionAsked: false,
  activeConversationId: null,
  sending: false
};

const elements = {
  conversationList: document.querySelector("#conversation-list"),
  conversationTitle: document.querySelector("#conversation-title"),
  emptyState: document.querySelector("#empty-state"),
  errorBanner: document.querySelector("#error-banner"),
  form: document.querySelector("#message-form"),
  input: document.querySelector("#message-input"),
  messages: document.querySelector("#messages"),
  newChat: document.querySelector("#new-chat"),
  enableNotifications: document.querySelector("#enable-notifications"),
  reminderList: document.querySelector("#reminder-list"),
  reminderToast: document.querySelector("#reminder-toast"),
  sendButton: document.querySelector("#send-button"),
  statusCard: document.querySelector("#status-card"),
  statusMessage: document.querySelector("#status-message"),
  statusTitle: document.querySelector("#status-title"),
  googleStatusCard: document.querySelector("#google-status-card"),
  googleStatusMessage: document.querySelector("#google-status-message"),
  googleConnect: document.querySelector("#google-connect"),
  googleDisconnect: document.querySelector("#google-disconnect")
};

async function request(url, options) {
  const response = await fetch(url, {
    headers: { "Content-Type": "application/json" },
    ...options
  });

  if (!response.ok) {
    let message = `Request failed with HTTP ${response.status}.`;
    try {
      const body = await response.json();
      message = body.error ?? message;
    } catch {
      // Keep the generic HTTP error when a response has no JSON body.
    }

    throw new Error(message);
  }

  return response.json();
}

async function streamMessage(conversationId, message, onDelta) {
  const response = await fetch(
    `/api/conversations/${conversationId}/messages/stream`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ message })
    });

  if (!response.body) {
    throw new Error("Streaming is not supported by this browser.");
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  let completedMessage = null;
  let streamError = null;

  const processLine = line => {
    if (!line.trim()) {
      return;
    }

    const event = JSON.parse(line);
    if (event.type === "delta") {
      onDelta(event.content ?? "");
    } else if (event.type === "complete") {
      completedMessage = event.message;
    } else if (event.type === "error") {
      streamError = event.error ?? "The response stream failed.";
    }
  };

  while (true) {
    const { value, done } = await reader.read();
    buffer += decoder.decode(value ?? new Uint8Array(), { stream: !done });

    const lines = buffer.split("\n");
    buffer = lines.pop() ?? "";
    for (const line of lines) {
      processLine(line);
    }

    if (done) {
      processLine(buffer);
      break;
    }
  }

  if (streamError) {
    throw new Error(streamError);
  }

  if (!response.ok) {
    throw new Error(`Request failed with HTTP ${response.status}.`);
  }

  if (!completedMessage) {
    throw new Error("The response stream ended before completion.");
  }

  return completedMessage;
}

async function loadStatus() {
  try {
    const status = await request("/api/status");
    elements.statusCard.classList.toggle("available", status.available);
    elements.statusTitle.textContent = status.available
      ? `${status.model} ready`
      : "Ollama setup needed";
    elements.statusMessage.textContent = status.available
      ? "Running locally"
      : status.message;
  } catch {
    elements.statusTitle.textContent = "Status unavailable";
    elements.statusMessage.textContent = "The application status endpoint could not be reached.";
  }
}

async function loadGoogleStatus() {
  try {
    const status = await request("/api/auth/google/status");
    elements.googleStatusCard.classList.toggle("available", Boolean(status.connected));
    elements.googleStatusMessage.textContent = status.message
      ?? (status.connected ? "Google Calendar connected." : "Not connected");
    elements.googleConnect.disabled = !status.configured;
    elements.googleConnect.title = status.configured
      ? "Connect Google Calendar"
      : "Set Google ClientId in User Secrets first";
    elements.googleConnect.hidden = Boolean(status.connected);
    elements.googleDisconnect.hidden = !status.connected;
  } catch {
    elements.googleStatusCard.classList.remove("available");
    elements.googleStatusMessage.textContent = "Google status could not be loaded.";
    elements.googleConnect.disabled = true;
    elements.googleConnect.title = "Set Google ClientId in User Secrets first";
    elements.googleConnect.hidden = false;
    elements.googleDisconnect.hidden = true;
  }
}

async function loadConversations(preferredId) {
  state.conversations = await request("/api/conversations");

  if (state.conversations.length === 0) {
    const conversation = await createConversation();
    state.conversations = [conversation];
  }

  const desiredId = preferredId ?? state.activeConversationId;
  const active = state.conversations.find(item => item.id === desiredId)
    ?? state.conversations[0];

  renderConversations();
  await selectConversation(active.id);
}

async function createConversation() {
  return request("/api/conversations", {
    method: "POST",
    body: JSON.stringify({ title: null })
  });
}

function renderConversations() {
  elements.conversationList.replaceChildren();

  for (const conversation of state.conversations) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "conversation-button";
    button.classList.toggle("active", conversation.id === state.activeConversationId);
    button.textContent = conversation.title;
    button.title = conversation.title;
    button.addEventListener("click", () => selectConversation(conversation.id));
    elements.conversationList.append(button);
  }
}

async function selectConversation(id) {
  state.activeConversationId = id;
  const conversation = state.conversations.find(item => item.id === id);
  elements.conversationTitle.textContent = conversation?.title ?? "Conversation";
  renderConversations();

  const messages = await request(`/api/conversations/${id}/messages`);
  renderMessages(messages);
  elements.input.focus();
}

function renderMessages(messages) {
  elements.messages.replaceChildren();

  if (messages.length === 0) {
    elements.messages.append(elements.emptyState);
    elements.emptyState.hidden = false;
    return;
  }

  for (const message of messages) {
    appendMessage(message.role, message.content);
  }

  scrollToLatest();
}

function appendMessage(role, content, pending = false) {
  elements.emptyState.hidden = true;
  const message = document.createElement("article");
  message.className = `message ${role}${pending ? " pending" : ""}`;

  const body = document.createElement("div");
  body.className = "message-content";
  body.textContent = content;
  message.append(body);
  elements.messages.append(message);
  scrollToLatest();
  return message;
}

function scrollToLatest() {
  elements.messages.scrollTop = elements.messages.scrollHeight;
}

function setSending(sending) {
  state.sending = sending;
  elements.input.disabled = sending;
  elements.sendButton.disabled = sending;
}

function showError(message) {
  elements.errorBanner.textContent = message;
  elements.errorBanner.hidden = false;
}

function clearError() {
  elements.errorBanner.hidden = true;
  elements.errorBanner.textContent = "";
}

elements.newChat.addEventListener("click", async () => {
  clearError();
  try {
    const conversation = await createConversation();
    await loadConversations(conversation.id);
  } catch (error) {
    showError(error.message);
  }
});

elements.form.addEventListener("submit", async event => {
  event.preventDefault();
  if (state.sending || !state.activeConversationId) {
    return;
  }

  const content = elements.input.value.trim();
  if (!content) {
    return;
  }

  clearError();
  elements.input.value = "";
  elements.input.style.height = "auto";
  appendMessage("user", content);
  const pending = appendMessage("assistant", "Thinking…", true);
  const pendingBody = pending.querySelector(".message-content");
  let receivedFirstChunk = false;
  setSending(true);

  try {
    await streamMessage(
      state.activeConversationId,
      content,
      chunk => {
        if (!receivedFirstChunk) {
          receivedFirstChunk = true;
          pending.classList.remove("pending");
          pendingBody.textContent = "";
        }

        pendingBody.textContent += chunk;
        scrollToLatest();
      });
    await Promise.all([
      loadConversations(state.activeConversationId),
      loadReminders()
    ]);
  } catch (error) {
    pending.remove();
    showError(error.message);
    await selectConversation(state.activeConversationId);
  } finally {
    setSending(false);
    elements.input.focus();
  }
});

elements.input.addEventListener("keydown", event => {
  if (event.key === "Enter" && !event.shiftKey) {
    event.preventDefault();
    elements.form.requestSubmit();
  }
});

elements.input.addEventListener("input", () => {
  elements.input.style.height = "auto";
  elements.input.style.height = `${elements.input.scrollHeight}px`;
});

Promise.all([loadStatus(), loadGoogleStatus(), loadConversations(), loadReminders()])
  .catch(error => showError(error.message));

setInterval(() => {
  loadReminders().catch(() => {
    // Keep the chat usable if reminder polling fails.
  });
}, 8000);

async function loadReminders() {
  state.reminders = await request("/api/reminders");
  renderReminders();
  await notifyFiredReminders();
}

function renderReminders() {
  elements.reminderList.replaceChildren();

  if (state.reminders.length === 0) {
    const empty = document.createElement("p");
    empty.className = "reminder-empty";
    empty.textContent = "No upcoming reminders.";
    elements.reminderList.append(empty);
    return;
  }

  for (const reminder of state.reminders) {
    const item = document.createElement("article");
    item.className = `reminder-item${reminder.status === "fired" ? " fired" : ""}`;

    const title = document.createElement("strong");
    title.textContent = reminder.title;

    const due = document.createElement("span");
    due.textContent = reminder.status === "fired"
      ? `Due now · ${formatDue(reminder.dueAtUtc)}`
      : formatDue(reminder.dueAtUtc);

    item.append(title, due);
    elements.reminderList.append(item);
  }
}

async function notifyFiredReminders() {
  const fired = state.reminders.filter(item => item.status === "fired");
  const unseen = fired.filter(item => !state.notifiedReminderIds.has(item.id));
  if (unseen.length === 0) {
    return;
  }

  for (const reminder of unseen) {
    state.notifiedReminderIds.add(reminder.id);
  }

  const latest = unseen[unseen.length - 1];
  elements.reminderToast.hidden = false;
  elements.reminderToast.textContent = `Reminder: ${latest.title}`;
  window.setTimeout(() => {
    elements.reminderToast.hidden = true;
  }, 8000);

  await showBrowserNotification(latest);
}

async function showBrowserNotification(reminder) {
  if (!("Notification" in window)) {
    return;
  }

  if (Notification.permission === "default" && !state.notificationPermissionAsked) {
    state.notificationPermissionAsked = true;
    await Notification.requestPermission();
  }

  if (Notification.permission !== "granted") {
    return;
  }

  new Notification(reminder.title, {
    body: reminder.status === "fired"
      ? `Due now · ${formatDue(reminder.dueAtUtc)}`
      : formatDue(reminder.dueAtUtc)
  });
}

async function requestNotificationPermission() {
  if (!("Notification" in window)) {
    showError("This browser does not support notifications.");
    updateNotificationButton("unsupported");
    return "unsupported";
  }

  state.notificationPermissionAsked = true;
  const permission = await Notification.requestPermission();
  updateNotificationButton(permission);
  return permission;
}

function updateNotificationButton(permission) {
  const currentPermission = permission
    ?? (("Notification" in window) ? Notification.permission : "unsupported");

  elements.enableNotifications.disabled = currentPermission === "granted"
    || currentPermission === "unsupported";
  elements.enableNotifications.textContent = currentPermission === "granted"
    ? "Notifications enabled"
    : currentPermission === "denied"
      ? "Notifications blocked"
      : currentPermission === "unsupported"
        ? "Notifications unavailable"
        : "Enable notifications";
}

elements.enableNotifications.addEventListener("click", async () => {
  clearError();
  try {
    const permission = await requestNotificationPermission();
    if (permission === "granted") {
      new Notification("Notifications enabled", {
        body: "Reminder alerts will appear here when they are due."
      });
    } else if (permission === "denied") {
      showError(
        "Notifications are blocked. Allow them in the browser site settings for localhost, then reload.");
    }
  } catch (error) {
    showError(error.message);
  }
});

elements.googleConnect.addEventListener("click", () => {
  window.location.href = "/api/auth/google/login";
});

elements.googleDisconnect.addEventListener("click", async () => {
  clearError();
  try {
    await request("/api/auth/google/logout", { method: "POST" });
    await loadGoogleStatus();
  } catch (error) {
    showError(error.message);
  }
});

function formatDue(value) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return date.toLocaleString(undefined, {
    timeZone: "Asia/Kolkata",
    timeZoneName: "short",
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit"
  });
}

updateNotificationButton();
