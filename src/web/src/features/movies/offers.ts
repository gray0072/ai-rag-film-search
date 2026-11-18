import type { OfferItem } from "@/api/types";

/** The cheapest active offer: the "from" price on a movie card. */
export function cheapestOffer(offers: readonly OfferItem[]): OfferItem | undefined {
  return offers.reduce<OfferItem | undefined>(
    (best, offer) => (best === undefined || offer.priceMinor < best.priceMinor ? offer : best),
    undefined,
  );
}

/** Buy first, then rent: the order of the offer buttons. */
export function sortOffers(offers: readonly OfferItem[]): OfferItem[] {
  return [...offers].sort((a, b) => (a.type === b.type ? 0 : a.type === "buy" ? -1 : 1));
}
