async function apiFetch(url, options = {}) {
  const res = await fetch(url, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  });
  const data = await res.json();
  return { ok: res.ok, status: res.status, ...data };
}

export const createPerson = (body) =>
  apiFetch('/api/persons', { method: 'POST', body: JSON.stringify(body) });

export const getPerson = (id) =>
  apiFetch(`/api/persons/${id}`);

export const updatePerson = (id, body) =>
  apiFetch(`/api/persons/${id}`, { method: 'PUT', body: JSON.stringify(body) });

export const deletePerson = (id) =>
  apiFetch(`/api/persons/${id}`, { method: 'DELETE' });
