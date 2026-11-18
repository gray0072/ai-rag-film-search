import type { components } from "./schema";

type Schemas = components["schemas"];

export type MovieSummary = Schemas["MovieSummary"];
export type MovieDetails = Schemas["MovieDetails"];
export type OfferItem = Schemas["OfferItem"];
export type OfferType = Schemas["OfferType"];
export type GenreItem = Schemas["GenreItem"];
export type CartView = Schemas["CartView"];
export type CartLine = Schemas["CartLine"];
export type OrderView = Schemas["OrderView"];
export type OrderStatus = Schemas["OrderStatus"];
export type LibraryItem = Schemas["LibraryItem"];
export type SearchRequest = Schemas["SearchRequest"];
export type SearchResponse = Schemas["SearchResponse"];
export type SearchFilters = Schemas["SearchFilters"];
export type SearchItem = Schemas["SearchItem"];
export type SearchDebug = Schemas["SearchDebug"];
export type UnderstoodQuery = Schemas["UnderstoodQuery"];
export type SearchMode = NonNullable<Schemas["SearchMode"]>;
