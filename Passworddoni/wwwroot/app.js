const form = document.querySelector('#passwordForm');
const list = document.querySelector('#list');
const message = document.querySelector('#message');
const dialog = document.querySelector('#revealDialog');
const output = document.querySelector('#revealOutput');
const searchInput = document.querySelector('#searchInput');
const searchButton = document.querySelector('#searchButton');
const resetButton = document.querySelector('#resetButton');
const resultCount = document.querySelector('#resultCount');
let passwordItems = [];

function renderItems() {
  const term = searchInput.value.trim().toLowerCase();
  const items = term
    ? passwordItems.filter(item => [item.title, item.username, item.password, item.note]
      .some(value => `sha256:${value || ''}`.toLowerCase().includes(term)))
    : passwordItems;

  resultCount.textContent = items.length === 0 ? '0 items' : `1-${items.length} of ${items.length}`;
  list.innerHTML = items.map(item => `
    <article class="password-card">
      <div class="password-card-header">
        <div>
          <span class="category-pill">Secure Entry</span>
          <h3>sha256:${item.title}</h3>
          <p class="password-description">user: sha256:${item.username}</p>
        </div>
        <div class="password-actions">
          <button class="btn btn-sm btn-outline-primary" type="button" data-reveal="${item.id}">Reveal details</button>
          <button class="btn btn-sm btn-outline-danger" type="button" data-delete="${item.id}">Delete</button>
        </div>
      </div>
      <pre><code>pass: sha256:${item.password}</code></pre>
    </article>
  `).join('') || '<div class="empty-state">No saved passwords. Add your first password or clear the search.</div>';
}

async function loadItems() {
  const response = await fetch('/api/passwords', { cache: 'no-store' });
  passwordItems = await response.json();
  renderItems();
}

form.addEventListener('submit', async event => {
  event.preventDefault();
  const data = Object.fromEntries(new FormData(form).entries());
  const response = await fetch('/api/passwords', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(data)
  });
  message.textContent = response.ok ? 'Password saved.' : 'Password could not be saved.';
  if (response.ok) {
    form.reset();
    await loadItems();
  }
});

searchButton.addEventListener('click', renderItems);
searchInput.addEventListener('input', renderItems);
resetButton.addEventListener('click', () => {
  searchInput.value = '';
  renderItems();
});

list.addEventListener('click', async event => {
  const revealButton = event.target.closest('[data-reveal]');
  const deleteButton = event.target.closest('[data-delete]');
  const revealId = revealButton?.dataset.reveal;
  const deleteId = deleteButton?.dataset.delete;
  if (revealId) {
    event.preventDefault();
    event.stopPropagation();
    const password = window.prompt('Enter the admin password to reveal these details:');
    if (!password) return;
    const response = await fetch(`/api/passwords/${revealId}/reveal`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ password })
    });
    if (!response.ok) {
      window.alert('The password is incorrect.');
      return;
    }
    const item = await response.json();
    output.textContent = `Title: ${item.title}\nUsername: ${item.username || ''}\nPassword: ${item.password}\nNote: ${item.note || ''}`;
    dialog.showModal();
  }
  if (deleteId) {
    event.preventDefault();
    event.stopPropagation();
    await fetch(`/api/passwords/${deleteId}`, { method: 'DELETE' });
    await loadItems();
  }
});

if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('/service-worker.js');
}

loadItems();
