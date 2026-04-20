import { createBrowserRouter, RouterProvider, Navigate, Outlet, Link } from 'react-router-dom';
import AddPersonPage from './pages/AddPersonPage';
import PersonProfilePage from './pages/PersonProfilePage';
import EditPersonPage from './pages/EditPersonPage';

function Layout() {
  return (
    <>
      <header className="app-header">
        <Link to="/persons/new" className="app-title">Family Tree</Link>
      </header>
      <main className="app-main">
        <Outlet />
      </main>
    </>
  );
}

const router = createBrowserRouter([
  {
    element: <Layout />,
    children: [
      { path: '/',                  element: <Navigate to="/persons/new" replace /> },
      { path: '/persons/new',       element: <AddPersonPage /> },
      { path: '/persons/:id',       element: <PersonProfilePage /> },
      { path: '/persons/:id/edit',  element: <EditPersonPage /> },
    ],
  },
]);

export default function App() {
  return <RouterProvider router={router} />;
}
