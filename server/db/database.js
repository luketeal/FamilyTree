const Database = require('better-sqlite3');
const path = require('path');

const db = new Database(path.join(__dirname, 'family_tree.db'));

// Enable WAL mode for better concurrent read performance
db.pragma('journal_mode = WAL');

db.exec(`
  CREATE TABLE IF NOT EXISTS persons (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    first_name  TEXT    NOT NULL,
    last_name   TEXT    NOT NULL,
    birth_date  TEXT,
    death_date  TEXT,
    gender      TEXT    NOT NULL DEFAULT 'Unknown'
                        CHECK(gender IN ('Male', 'Female', 'Non-binary', 'Unknown')),
    notes       TEXT,
    created_at  TEXT    NOT NULL DEFAULT (datetime('now')),
    updated_at  TEXT    NOT NULL DEFAULT (datetime('now'))
  )
`);

module.exports = db;
