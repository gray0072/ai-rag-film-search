import type { RouteObject } from "react-router";
import { CartPage } from "@/pages/CartPage";
import { CatalogPage } from "@/pages/CatalogPage";
import { CheckoutCancelPage } from "@/pages/CheckoutCancelPage";
import { ChatPage } from "@/pages/ChatPage";
import { CheckoutSuccessPage } from "@/pages/CheckoutSuccessPage";
import { RequireAuth } from "@/features/auth/RequireAuth";
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
      { path: "*", element: <NotFoundPage /> },
    ],
  },
];
