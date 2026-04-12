import { useState, useEffect } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import DeleteConfirmModal from '../components/DeleteConfirmModal';
import { getPerson, deletePerson } from '../api/persons';

function formatDate(dateStr) {
  if (!dateStr) return null;
  // dateStr is YYYY-MM-DD; display as a readable date without timezone conversion
  const [year, month, day] = dateStr.split('-');
  return new Date(Number(year), Number(month) - 1, Number(day))
    .toLocaleDateString('en-US', { year: 'numeric', month: 'long', day: 'numeric' });
}

function calcAge(birthDate, deathDate) {
  if (!birthDate) return null;
  const end = deathDate ? new Date(deathDate) : new Date();
  const start = new Date(birthDate);
  let age = end.getFullYear() - start.getFullYear();
  const monthDiff = end.getMonth() - start.getMonth();
  if (monthDiff < 0 || (monthDiff === 0 && end.getDate() < start.getDate())) {
    age -= 1;
  }
  return age;
}

function Initials({ firstName, lastName }) {
  return (
    <div className="profile-avatar" aria-hidden="true">
      {firstName[0]}{lastName[0]}
    </div>
  );
}

export default function PersonProfilePage() {
  const { id } = useParams();
  const navigate = useNavigate();

  const [person, setPerson] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [showDeleteModal, setShowDeleteModal] = useState(false);

  useEffect(() => {
    setLoading(true);
    getPerson(id)
      .then(result => {
        if (!result.ok) {
          setError(result.error || 'Failed to load person.');
        } else {
          setPerson(result.person);
        }
      })
      .catch(() => setError('Failed to load person.'))
      .finally(() => setLoading(false));
  }, [id]);

  async function handleDelete() {
    await deletePerson(id);
    navigate('/persons/new', { replace: true });
  }

  if (loading) return <div className="page"><p className="loading">Loading…</p></div>;
  if (error)   return <div className="page"><p className="error-text">{error}</p></div>;

  const isDeceased = Boolean(person.death_date);
  const age = calcAge(person.birth_date, person.death_date);

  return (
    <div className="page">
      <div className="profile-header">
        <Initials firstName={person.first_name} lastName={person.last_name} />
        <div className="profile-title">
          <h1>{person.first_name} {person.last_name}</h1>
          {isDeceased && <span className="badge badge--deceased">Deceased</span>}
        </div>
      </div>

      <dl className="profile-details">
        <dt>Gender</dt>
        <dd>{person.gender}</dd>

        {person.birth_date && (
          <>
            <dt>Born</dt>
            <dd>
              {formatDate(person.birth_date)}
              {age !== null && (
                <span className="age-note">
                  {' '}— {isDeceased ? `age ${age}` : `age ~${age}`}
                </span>
              )}
            </dd>
          </>
        )}

        {person.death_date && (
          <>
            <dt>Died</dt>
            <dd>{formatDate(person.death_date)}</dd>
          </>
        )}

        {person.notes && (
          <>
            <dt>Notes</dt>
            <dd className="profile-notes">{person.notes}</dd>
          </>
        )}
      </dl>

      <div className="profile-actions">
        <Link to={`/persons/${id}/edit`} className="btn btn--primary">Edit</Link>
        <button onClick={() => setShowDeleteModal(true)} className="btn btn--danger">Delete</button>
      </div>

      {showDeleteModal && (
        <DeleteConfirmModal
          personName={`${person.first_name} ${person.last_name}`}
          onConfirm={handleDelete}
          onCancel={() => setShowDeleteModal(false)}
        />
      )}
    </div>
  );
}
