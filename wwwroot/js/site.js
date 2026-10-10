(() => {
  try {
    if (typeof signalR === "undefined") return;
    // The bell is rendered only for signed-in users, and the hub requires sign-in: without it,
    // connecting would just fail with 401.
    const badge = document.getElementById("gharsBellBadge");
    if (!badge) return;
    const bump = () => {
      if (!badge) return;
      const next = (parseInt(badge.textContent || "0", 10) || 0) + 1;
      badge.textContent = next;
      badge.classList.remove("d-none");
    };
    const connection = new signalR.HubConnectionBuilder().withUrl("/hubs/notifications").withAutomaticReconnect().build();
    connection.on("notification", bump);
    connection.on("notificationReceived", bump);
    connection.start().catch(() => {});
  } catch { }
})();
