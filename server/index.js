const express = require('express');
const cors = require('cors');
const personsRouter = require('./routes/persons');

const app = express();

app.use(cors());
app.use(express.json());

app.use('/api/persons', personsRouter);

// Simple health check
app.get('/api/health', (_req, res) => res.json({ status: 'ok' }));

const PORT = process.env.PORT || 3001;
app.listen(PORT, () => {
  console.log(`Family Tree API running on http://localhost:${PORT}`);
});
