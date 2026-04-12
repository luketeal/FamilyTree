export default function DeleteConfirmModal({ personName, relationships = [], onConfirm, onCancel }) {
  return (
    <div className="modal-overlay" role="dialog" aria-modal="true" aria-labelledby="delete-modal-title">
      <div className="modal">
        <h2 id="delete-modal-title">Delete Person</h2>
        <p>
          Are you sure you want to permanently delete <strong>{personName}</strong>?
        </p>

        {relationships.length > 0 ? (
          <div className="modal-warning">
            <p>The following relationships will also be removed:</p>
            <ul>
              {relationships.map((r, i) => <li key={i}>{r}</li>)}
            </ul>
          </div>
        ) : (
          <p className="modal-note">This person has no relationships on record.</p>
        )}

        <p className="modal-warning">This action is permanent and cannot be undone.</p>

        <div className="modal-actions">
          <button onClick={onCancel} className="btn btn--secondary">Cancel</button>
          <button onClick={onConfirm} className="btn btn--danger">Delete Permanently</button>
        </div>
      </div>
    </div>
  );
}
