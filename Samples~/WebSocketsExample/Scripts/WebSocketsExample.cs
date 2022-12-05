using System;
using System.Threading.Tasks;
using H.Socket.IO;
using UnityEngine;

#nullable enable

public class WebSocketsExample : MonoBehaviour
{
	private class ChatMessage
	{
		public string? Username { get; set; }
		public string? Message { get; set; }
		public long NumUsers { get; set; }
	}

	void Start()
	{
		Task.Run(async () =>
		{
			var client = new SocketIoClient();

			client.Connected += (sender, args) => Console.WriteLine($"Connected: {args.Namespace}");
			client.Disconnected += (sender, args) => Console.WriteLine($"Disconnected. Reason: {args.Reason}, Status: {args.Status:G}");
			client.EventReceived += (sender, args) => Console.WriteLine($"EventReceived: Namespace: {args.Namespace}, Value: {args.Value}, IsHandled: {args.IsHandled}");
			client.HandledEventReceived += (sender, args) => Console.WriteLine($"HandledEventReceived: Namespace: {args.Namespace}, Value: {args.Value}");
			client.UnhandledEventReceived += (sender, args) => Console.WriteLine($"UnhandledEventReceived: Namespace: {args.Namespace}, Value: {args.Value}");
			client.ErrorReceived += (sender, args) => Console.WriteLine($"ErrorReceived: Namespace: {args.Namespace}, Value: {args.Value}");
			client.ExceptionOccurred += (sender, args) => Console.WriteLine($"ExceptionOccurred: {args.Value}");

			client.On<ChatMessage>("login", message =>
			{
				Debug.Log($"You are logged in. Total number of users: {message?.NumUsers}");
			});
			client.On<ChatMessage>("user joined", message =>
			{
				Debug.Log($"User joined: {message?.Username}. Total number of users: {message?.NumUsers}");
			});
			client.On<ChatMessage>("user left", message =>
			{
				Debug.Log($"User left: {message?.Username}. Total number of users: {message?.NumUsers}");
			});
			client.On<ChatMessage>("typing", message =>
			{
				Debug.Log($"User typing: {message?.Username}");
			});
			client.On<ChatMessage>("stop typing", message =>
			{
				Debug.Log($"User stop typing: {message?.Username}");
			});
			client.On<ChatMessage>("new message", message =>
			{
				Debug.Log($"New message from user \"{message?.Username}\": {message?.Message}");
			});

			await client.ConnectAsync(new Uri("wss://socketio-chat-h9jt.herokuapp.com/"));
			await client.Emit("add user", "C# Test User");
			await Task.Delay(TimeSpan.FromMilliseconds(200));
			await client.Emit("typing");
			await Task.Delay(TimeSpan.FromMilliseconds(200));
			await client.Emit("new message", "hello");
			await Task.Delay(TimeSpan.FromMilliseconds(200));
			await client.Emit("stop typing");
			await Task.Delay(TimeSpan.FromSeconds(2));
			await client.DisconnectAsync();

			client.Dispose();
		});
	}
}