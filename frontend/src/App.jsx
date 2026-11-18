import { useCallback, useEffect, useState } from "react";
import { createOrder, fetchOrders } from "./api/ordersApi";
import { useOrdersHub } from "./hooks/useOrdersHub";

const SUGGESTED_SKUS = [
  "LAPTOP-PRO-15",
  "MOUSE-ERG-01",
  "TECLADO-MEC-02",
  "MONITOR-4K-27",
];

const STATUS_LABELS = {
  Pending: "Pendiente",
  Confirmed: "Confirmado",
  Rejected: "Rechazado",
};

const HUB_STATUS_LABELS = {
  connecting: "Conectando tiempo real...",
  connected: "En vivo (SignalR)",
  reconnecting: "Reconectando...",
  disconnected: "Sin conexión en vivo",
};

export default function App() {
  const [orders, setOrders] = useState([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState("");

  const [clienteNombre, setClienteNombre] = useState("");
  const [sku, setSku] = useState(SUGGESTED_SKUS[0]);
  const [cantidad, setCantidad] = useState(1);
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState("");

  const loadOrders = useCallback(async () => {
    try {
      setLoadError("");
      const data = await fetchOrders();
      setOrders(data);
    } catch (err) {
      setLoadError(err.message || "Error al cargar los pedidos.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadOrders();
  }, [loadOrders]);

  const handleOrderUpdated = useCallback((updatedOrder) => {
    setOrders((prev) => {
      const exists = prev.some((o) => o.id === updatedOrder.id);
      if (!exists) return [updatedOrder, ...prev];
      return prev.map((o) => (o.id === updatedOrder.id ? updatedOrder : o));
    });
  }, []);

  const hubState = useOrdersHub(handleOrderUpdated, loadOrders);

  async function handleSubmit(e) {
    e.preventDefault();
    setFormError("");

    const nombreTrimmed = clienteNombre.trim();
    const skuTrimmed = sku.trim().toUpperCase();
    const qty = Number(cantidad);

    if (!nombreTrimmed) {
      setFormError("El nombre del cliente es obligatorio.");
      return;
    }
    if (!skuTrimmed) {
      setFormError("El SKU es obligatorio.");
      return;
    }
    if (!Number.isInteger(qty) || qty < 1 || qty > 100) {
      setFormError("La cantidad debe ser un entero entre 1 y 100.");
      return;
    }

    setSubmitting(true);
    try {
      const created = await createOrder({
        clienteNombre: nombreTrimmed,
        sku: skuTrimmed,
        cantidad: qty,
      });

      setOrders((prev) => {
        if (prev.some((o) => o.id === created.id)) return prev;
        return [created, ...prev];
      });

      setClienteNombre("");
      setCantidad(1);
    } catch (err) {
      setFormError(err.message || "No se pudo crear el pedido.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="container">
      <header className="header">
        <div>
          <h1>OrderFlow</h1>
          <p className="subtitle">Gestión de pedidos y reserva asíncrona de inventario</p>
        </div>
        <span className={`hub-badge hub-${hubState}`}>
          {HUB_STATUS_LABELS[hubState] || hubState}
        </span>
      </header>

      <main className="grid">
        <section className="card">
          <h2>Nuevo pedido</h2>
          <form onSubmit={handleSubmit} noValidate>
            <div className="field">
              <label htmlFor="clienteNombre">Nombre del cliente</label>
              <input
                id="clienteNombre"
                type="text"
                value={clienteNombre}
                onChange={(e) => setClienteNombre(e.target.value)}
                placeholder="Ej. Ana Gómez"
                maxLength={120}
              />
            </div>

            <div className="field">
              <label htmlFor="sku">SKU del producto</label>
              <input
                id="sku"
                type="text"
                list="sku-suggestions"
                value={sku}
                onChange={(e) => setSku(e.target.value)}
                placeholder="Ej. LAPTOP-PRO-15"
                maxLength={64}
              />
              <datalist id="sku-suggestions">
                {SUGGESTED_SKUS.map((item) => (
                  <option key={item} value={item} />
                ))}
              </datalist>
            </div>

            <div className="field">
              <label htmlFor="cantidad">Cantidad (1 - 100)</label>
              <input
                id="cantidad"
                type="number"
                min={1}
                max={100}
                value={cantidad}
                onChange={(e) => setCantidad(e.target.value)}
              />
            </div>

            {formError && (
              <div className="alert alert-error" role="alert">
                {formError}
              </div>
            )}

            <button type="submit" className="btn" disabled={submitting}>
              {submitting ? "Enviando..." : "Crear pedido"}
            </button>
          </form>
        </section>

        <section className="card">
          <div className="card-header">
            <h2>Pedidos recientes</h2>
            <button type="button" className="btn-secondary" onClick={loadOrders}>
              Actualizar
            </button>
          </div>

          {loadError && (
            <div className="alert alert-error" role="alert">
              {loadError}
            </div>
          )}

          {loading ? (
            <p className="muted">Cargando pedidos...</p>
          ) : orders.length === 0 ? (
            <p className="muted">Aún no hay pedidos registrados.</p>
          ) : (
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Cliente</th>
                    <th>SKU</th>
                    <th>Cant.</th>
                    <th>Estado</th>
                    <th>Detalle</th>
                    <th>Hora</th>
                  </tr>
                </thead>
                <tbody>
                  {orders.map((order) => (
                    <tr key={order.id}>
                      <td>{order.clienteNombre}</td>
                      <td>
                        <code>{order.sku}</code>
                      </td>
                      <td>{order.cantidad}</td>
                      <td>
                        <span className={`badge badge-${order.status.toLowerCase()}`}>
                          {STATUS_LABELS[order.status] || order.status}
                        </span>
                      </td>
                      <td className="detail-cell">
                        {order.rejectionReason || "—"}
                      </td>
                      <td className="muted">
                        {new Date(order.updatedAt).toLocaleTimeString()}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      </main>
    </div>
  );
}
