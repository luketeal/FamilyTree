const express = require('express');
const router = express.Router();
const db = require('../db/database');

const VALID_GENDERS = ['Male', 'Female', 'Non-binary', 'Unknown'];

function validate(data) {
  const errors = {};

  if (!data.first_name || !String(data.first_name).trim()) {
    errors.first_name = 'First name is required';
  }
  if (!data.last_name || !String(data.last_name).trim()) {
    errors.last_name = 'Last name is required';
  }
  if (data.gender && !VALID_GENDERS.includes(data.gender)) {
    errors.gender = `Gender must be one of: ${VALID_GENDERS.join(', ')}`;
  }
  if (data.birth_date && isNaN(Date.parse(data.birth_date))) {
    errors.birth_date = 'Invalid birth date';
  }
  if (data.death_date && isNaN(Date.parse(data.death_date))) {
    errors.death_date = 'Invalid death date';
  }
  if (data.birth_date && data.death_date &&
      new Date(data.birth_date) >= new Date(data.death_date)) {
    errors.death_date = 'Death date must be after birth date';
  }
  if (data.notes && data.notes.length > 5000) {
    errors.notes = 'Notes must be 5,000 characters or fewer';
  }

  return errors;
}

// POST /api/persons — create a new person (US-001)
router.post('/', (req, res) => {
  const { first_name, last_name, birth_date, death_date, gender = 'Unknown', notes } = req.body;

  const errors = validate(req.body);
  if (Object.keys(errors).length > 0) {
    return res.status(422).json({ errors });
  }

  // Duplicate check: same full name + birth date (US-001)
  let duplicate = null;
  if (birth_date) {
    duplicate = db.prepare(
      'SELECT id, first_name, last_name FROM persons WHERE first_name = ? AND last_name = ? AND birth_date = ?'
    ).get(first_name.trim(), last_name.trim(), birth_date);
  }

  const result = db.prepare(`
    INSERT INTO persons (first_name, last_name, birth_date, death_date, gender, notes)
    VALUES (?, ?, ?, ?, ?, ?)
  `).run(
    first_name.trim(),
    last_name.trim(),
    birth_date || null,
    death_date || null,
    gender,
    notes || null
  );

  const person = db.prepare('SELECT * FROM persons WHERE id = ?').get(result.lastInsertRowid);

  const response = { person };
  if (duplicate) {
    response.warning = `A person named ${duplicate.first_name} ${duplicate.last_name} with the same birth date already exists.`;
    response.duplicate_id = duplicate.id;
  }

  res.status(201).json(response);
});

// GET /api/persons/:id — fetch a person's profile (US-002)
router.get('/:id', (req, res) => {
  const person = db.prepare('SELECT * FROM persons WHERE id = ?').get(req.params.id);
  if (!person) {
    return res.status(404).json({ error: 'Person not found' });
  }
  res.json({ person });
});

// PUT /api/persons/:id — update a person's details (US-003)
router.put('/:id', (req, res) => {
  const existing = db.prepare('SELECT id FROM persons WHERE id = ?').get(req.params.id);
  if (!existing) {
    return res.status(404).json({ error: 'Person not found' });
  }

  const { first_name, last_name, birth_date, death_date, gender = 'Unknown', notes } = req.body;

  const errors = validate(req.body);
  if (Object.keys(errors).length > 0) {
    return res.status(422).json({ errors });
  }

  db.prepare(`
    UPDATE persons
    SET first_name = ?, last_name = ?, birth_date = ?, death_date = ?,
        gender = ?, notes = ?, updated_at = datetime('now')
    WHERE id = ?
  `).run(
    first_name.trim(),
    last_name.trim(),
    birth_date || null,
    death_date || null,
    gender,
    notes || null,
    req.params.id
  );

  const person = db.prepare('SELECT * FROM persons WHERE id = ?').get(req.params.id);
  res.json({ person });
});

// DELETE /api/persons/:id — remove a person and their relationships (US-004)
router.delete('/:id', (req, res) => {
  const person = db.prepare('SELECT * FROM persons WHERE id = ?').get(req.params.id);
  if (!person) {
    return res.status(404).json({ error: 'Person not found' });
  }

  // Relationship cleanup will be added here as Epics 2–4 are implemented.
  // For now only the person record is deleted.
  db.prepare('DELETE FROM persons WHERE id = ?').run(req.params.id);

  res.json({ message: 'Person deleted successfully' });
});

module.exports = router;
