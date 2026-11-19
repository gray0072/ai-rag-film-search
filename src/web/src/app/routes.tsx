import type { RouteObject } from "react-router";
import { CartPage } from "@/pages/CartPage";
import { CatalogPage } from "@/pages/CatalogPage";
import { CheckoutCancelPage } from "@/pages/CheckoutCancelPage";
import { ChatPage } from "@/pages/ChatPage";
import { CheckoutSuccessPage } from "@/pages/CheckoutSuccessPage";
import { AdminLayout } from "@/features/admin/AdminLayout";
import { RequireAuth } from "@/features/auth/RequireAuth";
import { AdminAiPage } from "@/pages/admin/AdminAiPage";
import { AdminCatalogPage } from "@/pages/admin/AdminCatalogPage";
import { AdminEmbeddingsPage } from "@/pages/admin/AdminEmbeddingsPage";
import { AdminJobPage } from "@/pages/admin/AdminJobPage";
import { AdminJobsPage } from "@/pages/admin/AdminJobsPage";
import { LibraryPage } from "@/pages/LibraryPage";
import { LoginPage } from "@/pages/LoginPage";
import { MovieDetailsPage } from "@/pages/MovieDetailsPage";
import { NotFoundPage } from "@/pages/NotFoundPage";
import { RegisterPage } from "@/pages/RegisterPage";
import { SearchPage } from "@/pages/SearchPage";
import { AppLayout } from "./layout/AppLayout";

export const routes: RouteObject[] = [
  {
    element: <AppLayout />,
    children: [
      { index: true, element: <CatalogPage /> },
      { path: "movies/:id", element: <MovieDetailsPage /> },
      { path: "search", element: <SearchPage /> },
      { path: "cart", element: <CartPage /> },
      { path: "checkout/success", element: <CheckoutSuccessPage /> },
      { path: "checkout/cancel", element: <CheckoutCancelPage /> },
      {
        path: "library",
        element: (
          <RequireAuth>
            <LibraryPage />
          </RequireAuth>
        ),
      },
      { path: "login", element: <LoginPage /> },
      { path: "register", element: <RegisterPage /> },
      { path: "chat/:id?", element: <ChatPage /> },
      {
        // Admin pages (I22): AdminLayout lets only users with the admin role in.
        path: "admin",
        element: <AdminLayout />,
        children: [
          { index: true, element: <AdminCatalogPage /> },
          { path: "jobs", element: <AdminJobsPage /> },
          { path: "jobs/:id", element: <AdminJobPage /> },
          { path: "embeddings", element: <AdminEmbeddingsPage /> },
          { path: "ai", element: <AdminAiPage /> },
        ],
      },
      { path: "*", element: <NotFoundPage /> },
    ],
  },
];
