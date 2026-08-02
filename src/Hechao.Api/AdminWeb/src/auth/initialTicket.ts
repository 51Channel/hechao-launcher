let initialTicket: string | null = null;

const ticket = new URLSearchParams(window.location.hash.slice(1)).get("ticket");
if (ticket) {
  initialTicket = ticket;
  window.history.replaceState(
    window.history.state,
    "",
    `${window.location.pathname}${window.location.search}`
  );
}

export function takeInitialAdminTicket(): string | null {
  const ticketToRedeem = initialTicket;
  initialTicket = null;
  return ticketToRedeem;
}
