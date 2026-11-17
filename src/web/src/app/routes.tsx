import type { RouteObject } from "react-router";
import { CartPage } from "@/pages/CartPage";
import { CatalogPage } from "@/pages/CatalogPage";
import { ChatPage } from "@/pages/ChatPage";
import { CheckoutSuccessPage } from "@/pages/CheckoutSuccessPage";
import { LibraryPage } from "@/pages/LibraryPage";
import { MovieDetailsPage } from "@/pages/MovieDetailsPage";
import { NotFoundPage } from "@/pages/NotFoundPage";
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
      { path: "library", element: <LibraryPage /> },
      { path: "chat", element: <ChatPage /> },
      { path: "*", element: <NotFoundPage /> },
    ],
  },
];
