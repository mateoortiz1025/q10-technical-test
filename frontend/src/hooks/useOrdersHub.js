import { useEffect, useRef, useState } from "react";
import * as signalR from "@microsoft/signalr";

export function useOrdersHub(onOrderUpdated, onReconnected) {
  const [connectionState, setConnectionState] = useState("connecting");
  const onOrderUpdatedRef = useRef(onOrderUpdated);
  const onReconnectedRef = useRef(onReconnected);

  onOrderUpdatedRef.current = onOrderUpdated;
  onReconnectedRef.current = onReconnected;

  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl("/hubs/orders")
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    connection.on("OrderStatusChanged", (updatedOrder) => {
      onOrderUpdatedRef.current?.(updatedOrder);
    });

    connection.onreconnecting(() => setConnectionState("reconnecting"));
    connection.onreconnected(() => {
      setConnectionState("connected");
      onReconnectedRef.current?.();
    });
    connection.onclose(() => setConnectionState("disconnected"));

    connection
      .start()
      .then(() => setConnectionState("connected"))
      .catch(() => setConnectionState("disconnected"));

    return () => {
      connection.stop();
    };
  }, []);

  return connectionState;
}
