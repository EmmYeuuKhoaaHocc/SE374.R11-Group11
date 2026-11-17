import { createBrowserRouter, Navigate } from "react-router";
import { AuthGuard } from "../../features/auth/components/AuthGuard";
import { AuthCallbackPage } from "../../features/auth/pages/AuthCallbackPage";
import { LoginPage } from "../../features/auth/pages/LoginPage";
import { AppLayout } from "../components/AppLayout";
import { AuthHomePage } from "../pages/AuthHomePage";

export const router = createBrowserRouter([
  {
    path: "/login",
    element: <LoginPage />,
  },
  {
    path: "/auth/callback",
    element: <AuthCallbackPage />,
  },
  {
    path: "/",
    element: (
      <AuthGuard>
        <AppLayout />
      </AuthGuard>
    ),
    children: [
      {
        index: true,
        element: <AuthHomePage />,
      },
    ],
  },
  {
    path: "*",
    element: <Navigate to="/" replace />,
  },
]);
