import * as signalR from "@microsoft/signalr";

let connection = null;

export async function startSignalRConnection(negotiateUrl, dotNetHelper) {
    debugger;
    const response = await fetch(negotiateUrl);
    const info = await response.json();

    connection = new signalR.HubConnectionBuilder()
        .withUrl(info.url, { accessTokenFactory: () => info.accessToken })
        .withAutomaticReconnect()
        .build();

    connection.on("newMessage", (message) => {
        console.log("Message received:", message);
        if (dotNetHelper) {
            dotNetHelper.invokeMethodAsync("OnNewMessage", message);
        }
    });

    await connection.start();
    console.log("SignalR connected.");
}

export function stopSignalRConnection() {
    if (connection) {
        connection.stop();
    }
}
