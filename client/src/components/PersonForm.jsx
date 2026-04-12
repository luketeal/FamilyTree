import { useState, useEffect } from 'react';
import { useBlocker } from 'react-router-dom';

const GENDER_OPTIONS = ['Male', 'Female', 'Non-binary', 'Unknown'];
const MAX_NOTES = 5000;

const EMPTY_FORM = {
  first_name: '',
  last_name: '',
  birth_date: '',
  death_date: '',
  gender: 'Unknown',
  notes: '',
};

export default function PersonForm({ initialValues = {}, onSubmit, submitLabel = 'Save', serverErrors = {} }) {
  const [form, setForm] = useState({ ...EMPTY_FORM, ...initialValues });
  const [errors, setErrors] = useState({});
  const [submitting, setSubmitting] = useState(false);
  const [dirty, setDirty] = useState(false);

  // Merge server-side field errors into local error state
  useEffect(() => {
    if (Object.keys(serverErrors).length > 0) {
      setErrors(prev => ({ ...prev, ...serverErrors }));
    }
  }, [serverErrors]);

  // Block in-app navigation when form has unsaved changes (US-003)
  const blocker = useBlocker(({ currentLocation, nextLocation }) =>
    dirty && currentLocation.pathname !== nextLocation.pathname
  );

  // Also catch browser-level navigation (tab close, external link)
  useEffect(() => {
    const handleBeforeUnload = (e) => {
      if (dirty) {
        e.preventDefault();
        e.returnValue = '';
      }
    };
    window.addEventListener('beforeunload', handleBeforeUnload);
    return () => window.removeEventListener('beforeunload', handleBeforeUnload);
  }, [dirty]);

  function handleChange(e) {
    const { name, value } = e.target;
    setForm(prev => ({ ...prev, [name]: value }));
    setDirty(true);
    setErrors(prev => ({ ...prev, [name]: undefined }));
  }

  function validate() {
    const errs = {};
    if (!form.first_name.trim()) errs.first_name = 'First name is required';
    if (!form.last_name.trim()) errs.last_name = 'Last name is required';
    if (form.birth_date && isNaN(Date.parse(form.birth_date))) {
      errs.birth_date = 'Invalid birth date';
    }
    if (form.death_date && isNaN(Date.parse(form.death_date))) {
      errs.death_date = 'Invalid death date';
    }
    if (form.birth_date && form.death_date &&
        new Date(form.birth_date) >= new Date(form.death_date)) {
      errs.death_date = 'Death date must be after birth date';
    }
    if (form.notes.length > MAX_NOTES) {
      errs.notes = `Notes must be ${MAX_NOTES.toLocaleString()} characters or fewer`;
    }
    return errs;
  }

  async function handleSubmit(e) {
    e.preventDefault();
    const errs = validate();
    if (Object.keys(errs).length > 0) {
      setErrors(errs);
      return;
    }
    setSubmitting(true);
    try {
      await onSubmit(form);
      setDirty(false);
    } catch (err) {
      setErrors(prev => ({ ...prev, submit: err.message }));
    } finally {
      setSubmitting(false);
    }
  }

  const notesRemaining = MAX_NOTES - form.notes.length;
  const notesNearLimit = notesRemaining < 500;

  return (
    <>
      <form onSubmit={handleSubmit} className="person-form" noValidate>
        <div className="form-row">
          <div className="form-group">
            <label htmlFor="first_name">First Name <span className="required">*</span></label>
            <input
              id="first_name"
              name="first_name"
              type="text"
              value={form.first_name}
              onChange={handleChange}
              className={errors.first_name ? 'input-error' : ''}
              autoComplete="given-name"
            />
            {errors.first_name && <span className="field-error">{errors.first_name}</span>}
          </div>
          <div className="form-group">
            <label htmlFor="last_name">Last Name <span className="required">*</span></label>
            <input
              id="last_name"
              name="last_name"
              type="text"
              value={form.last_name}
              onChange={handleChange}
              className={errors.last_name ? 'input-error' : ''}
              autoComplete="family-name"
            />
            {errors.last_name && <span className="field-error">{errors.last_name}</span>}
          </div>
        </div>

        <div className="form-row">
          <div className="form-group">
            <label htmlFor="birth_date">Birth Date</label>
            <input
              id="birth_date"
              name="birth_date"
              type="date"
              value={form.birth_date}
              onChange={handleChange}
              className={errors.birth_date ? 'input-error' : ''}
            />
            {errors.birth_date && <span className="field-error">{errors.birth_date}</span>}
          </div>
          <div className="form-group">
            <label htmlFor="death_date">Death Date</label>
            <input
              id="death_date"
              name="death_date"
              type="date"
              value={form.death_date}
              onChange={handleChange}
              className={errors.death_date ? 'input-error' : ''}
            />
            {errors.death_date && <span className="field-error">{errors.death_date}</span>}
          </div>
        </div>

        <div className="form-group">
          <label htmlFor="gender">Gender</label>
          <select
            id="gender"
            name="gender"
            value={form.gender}
            onChange={handleChange}
          >
            {GENDER_OPTIONS.map(g => <option key={g} value={g}>{g}</option>)}
          </select>
        </div>

        <div className="form-group">
          <label htmlFor="notes">Notes</label>
          <textarea
            id="notes"
            name="notes"
            rows={5}
            value={form.notes}
            onChange={handleChange}
            className={errors.notes ? 'input-error' : ''}
            placeholder="Biographical details, sources, stories…"
          />
          <span className={`char-count ${notesNearLimit ? 'char-count--warn' : ''}`}>
            {form.notes.length.toLocaleString()} / {MAX_NOTES.toLocaleString()}
          </span>
          {errors.notes && <span className="field-error">{errors.notes}</span>}
        </div>

        {errors.submit && <p className="form-error">{errors.submit}</p>}

        <div className="form-actions">
          <button type="submit" disabled={submitting} className="btn btn--primary">
            {submitting ? 'Saving…' : submitLabel}
          </button>
        </div>
      </form>

      {/* In-app navigation blocker (US-003) */}
      {blocker.state === 'blocked' && (
        <div className="modal-overlay">
          <div className="modal">
            <h2>Unsaved Changes</h2>
            <p>You have unsaved changes. Are you sure you want to leave this page?</p>
            <div className="modal-actions">
              <button onClick={() => blocker.reset()} className="btn btn--secondary">
                Stay on Page
              </button>
              <button onClick={() => blocker.proceed()} className="btn btn--danger">
                Leave Without Saving
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
