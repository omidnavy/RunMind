'use strict';

document.addEventListener('DOMContentLoaded', () => {
  const form = document.querySelector('#request-form');
  const nameField = document.querySelector('#request-name');
  const priorityField = document.querySelector('#request-priority');
  const status = document.querySelector('#submission-status');
  const list = document.querySelector('#request-list');
  const emptyState = document.querySelector('#empty-state');
  let requestCount = 0;
  let resetFollowsSubmission = false;

  form.addEventListener('submit', event => {
    event.preventDefault();
    const name = nameField.value.trim();
    if (!name) {
      nameField.focus();
      status.textContent = 'Enter a request name before submitting.';
      return;
    }

    requestCount += 1;
    emptyState?.remove();
    const item = document.createElement('li');
    const title = document.createElement('strong');
    const detail = document.createElement('span');
    title.textContent = name;
    detail.textContent = `Priority: ${priorityField.value} · Request #${requestCount}`;
    item.append(title, detail);
    list.append(item);
    status.textContent = `Request submitted: ${name}.`;
    resetFollowsSubmission = true;
    form.reset();
    nameField.focus();
  });

  form.addEventListener('reset', () => {
    if (resetFollowsSubmission) {
      resetFollowsSubmission = false;
      return;
    }
    status.textContent = 'Form cleared.';
  });

  const corsProbe = document.querySelector('#cors-probe');
  corsProbe?.addEventListener('click', async () => {
    status.textContent = 'Checking browser cross-origin enforcement…';
    try {
      await fetch('http://127.0.0.1:4321/cors-probe');
      status.textContent = 'The cross-origin request was unexpectedly readable.';
    } catch (_) {
      status.textContent = 'The browser correctly blocked the cross-origin response.';
    }
  });
});
