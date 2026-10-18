// Reports which Claude Code tabs are in view to the StatusLight daemon, so it can tell finished sessions you've seen
// from ones you haven't. All the logic lives in the daemon; this only reports what's on screen.
const vscode = require('vscode');
const crypto = require('crypto');

// The daemon treats a window's report as stale after 90s, so heartbeat well inside that.
const HEARTBEAT_MS = 30_000;

const windowId = crypto.randomUUID();
let lastBody;

function isClaudeTab(tab) {
  return tab.input instanceof vscode.TabInputWebview && tab.input.viewType.includes('claudeVSCodePanel');
}

/** Titles of the Claude tabs on screen: the active tab of each editor group, and only while this window has focus. */
function titlesInView() {
  if (!vscode.window.state.focused) {
    return [];
  }
  return vscode.window.tabGroups.all
    .map((group) => group.activeTab)
    .filter((tab) => tab && isClaudeTab(tab))
    .map((tab) => tab.label);
}

async function send(titles) {
  const body = JSON.stringify({ windowId, titles });
  lastBody = body;
  const url = vscode.workspace.getConfiguration('statuslight').get('daemonUrl');
  try {
    await fetch(`${url}/vscode/view`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body,
      signal: AbortSignal.timeout(1000),
    });
  } catch {
    // Daemon isn't running; the next change or heartbeat tries again.
  }
}

function reportIfChanged() {
  const titles = titlesInView();
  if (JSON.stringify({ windowId, titles }) !== lastBody) {
    void send(titles);
  }
}

function activate(context) {
  context.subscriptions.push(
    vscode.window.onDidChangeWindowState(reportIfChanged),
    vscode.window.tabGroups.onDidChangeTabs(reportIfChanged),
    vscode.window.tabGroups.onDidChangeTabGroups(reportIfChanged),
  );

  const heartbeat = setInterval(() => void send(titlesInView()), HEARTBEAT_MS);
  context.subscriptions.push({ dispose: () => clearInterval(heartbeat) });

  void send(titlesInView());
}

function deactivate() {
  // Stop counting as "viewing" straight away rather than waiting for the report to go stale.
  return send([]);
}

module.exports = { activate, deactivate };
