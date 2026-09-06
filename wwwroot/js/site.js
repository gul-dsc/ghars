(() => {
  try {
    if (typeof signalR === "undefined") return;
    const badge = document.getElementById("gharsBellBadge");
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
