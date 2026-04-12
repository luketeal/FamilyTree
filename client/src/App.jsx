import { Routes, Route, Navigate, Link } from 'react-router-dom';
import AddPersonPage from './pages/AddPersonPage';
import PersonProfilePage from './pages/PersonProfilePage';
import EditPersonPage from './pages/EditPersonPage';

export default function App() {
  return (
    <>
      <header className="app-header">
        <Link to="/persons/new" className="app-title">Family Tree</Link>
      </header>
      <main className="app-main">
        <Routes>
          <Route path="/" element={<Navigate to="/persons/new" replace />} />
          <Route path="/persons/new" element={<AddPersonPage />} />
          <Route path="/persons/:id" element={<PersonProfilePage />} />
          <Route path="/persons/:id/edit" element={<EditPersonPage />} />
        </Routes>
      </main>
    </>
  );
}
