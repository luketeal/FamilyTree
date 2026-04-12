import { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import PersonForm from '../components/PersonForm';
import { getPerson, updatePerson } from '../api/persons';

export default function EditPersonPage() {
  const { id } = useParams();
  const navigate = useNavigate();

  const [person, setPerson] = useState(null);
  const [loading, setLoading] = useState(true);
  const [serverErrors, setServerErrors] = useState({});

  useEffect(() => {
    getPerson(id)
      .then(result => {
        if (result.ok) setPerson(result.person);
      })
      .finally(() => setLoading(false));
  }, [id]);

  async function handleSubmit(formData) {
    const result = await updatePerson(id, formData);

    if (!result.ok) {
      if (result.errors) {
        setServerErrors(result.errors);
        throw new Error('Please fix the errors above and try again.');
      }
      throw new Error(result.error || 'Failed to save changes.');
    }

    navigate(`/persons/${id}`);
  }

  if (loading) return <div className="page"><p className="loading">Loading…</p></div>;
  if (!person)  return <div className="page"><p className="error-text">Person not found.</p></div>;

  // Normalise null values to empty strings for the controlled form inputs
  const initialValues = {
    ...person,
    birth_date: person.birth_date ?? '',
    death_date: person.death_date ?? '',
    notes:      person.notes      ?? '',
  };

  return (
    <div className="page">
      <h1>Edit {person.first_name} {person.last_name}</h1>
      <PersonForm
        initialValues={initialValues}
        onSubmit={handleSubmit}
        submitLabel="Save Changes"
        serverErrors={serverErrors}
      />
    </div>
  );
}
