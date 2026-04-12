import { useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import PersonForm from '../components/PersonForm';
import { createPerson } from '../api/persons';

export default function AddPersonPage() {
  const navigate = useNavigate();
  const [duplicateWarning, setDuplicateWarning] = useState(null);
  const [serverErrors, setServerErrors] = useState({});

  async function handleSubmit(formData) {
    const result = await createPerson(formData);

    if (!result.ok) {
      if (result.errors) {
        setServerErrors(result.errors);
        throw new Error('Please fix the errors above and try again.');
      }
      throw new Error(result.error || 'Failed to save person.');
    }

    if (result.warning) {
      // Person was saved but a duplicate was detected — let the user decide (US-001)
      setDuplicateWarning({
        message: result.warning,
        duplicateId: result.duplicate_id,
        newId: result.person.id,
      });
      return;
    }

    navigate(`/persons/${result.person.id}`);
  }

  if (duplicateWarning) {
    return (
      <div className="page">
        <div className="warning-card">
          <h2>Possible Duplicate</h2>
          <p>{duplicateWarning.message}</p>
          <div className="warning-actions">
            <Link to={`/persons/${duplicateWarning.duplicateId}`} className="btn btn--secondary">
              View Existing Person
            </Link>
            <Link to={`/persons/${duplicateWarning.newId}`} className="btn btn--primary">
              Keep New Record Anyway
            </Link>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="page">
      <h1>Add Person</h1>
      <PersonForm onSubmit={handleSubmit} submitLabel="Add Person" serverErrors={serverErrors} />
    </div>
  );
}
