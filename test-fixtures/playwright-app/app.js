'use strict';

document.addEventListener('DOMContentLoaded', () => {
  const form = document.querySelector('#request-form');
  const nameField = document.querySelector('#request-name');
  const priorityField = document.querySelector('#request-priority');
  const status = document.querySelector('#submission-status');
  const list = document.querySelector('#request-list');
  const emptyState = document.querySelector('#empty-state');
  let requestCount = 0;

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
    form.reset();
    nameField.focus();
  });

  form.addEventListener('reset', () => {
    status.textContent = 'Form cleared.';
  });
});
