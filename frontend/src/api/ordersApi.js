export async function fetchOrders() {
  const response = await fetch("/api/orders");
  if (!response.ok) {
    throw new Error("No fue posible cargar los pedidos.");
  }
  return response.json();
}

export async function createOrder(payload) {
  const response = await fetch("/api/orders", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });

  if (response.ok) {
    return response.json();
  }

  let errorBody = null;
  try {
    errorBody = await response.json();
  } catch {
    // Response is not JSON
  }

  if (errorBody?.errors) {
    const messages = Object.values(errorBody.errors).flat();
    throw new Error(messages.join(" "));
  }

  throw new Error(
    errorBody?.detail ||
      errorBody?.title ||
      `Error al crear el pedido (HTTP ${response.status}).`
  );
}
