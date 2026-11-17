import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "@neuroscan/ui/styles.css";
import "./styles/theme.css";
import { AppProviders } from "./app/providers.tsx";
import { RouterProvider } from "react-router";
import { router } from "./app/router/create-router.tsx";
import "./app/register-resources.ts";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <AppProviders>
      <RouterProvider router={router} />
    </AppProviders>
  </StrictMode>,
);
