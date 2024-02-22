using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

class ServerObject
{
    private TcpListener tcpListener = new TcpListener(IPAddress.Any, 8888);
    private List<ClientObject> clients = new List<ClientObject>();
    private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

    protected internal void RemoveConnection(string id)
    {
        ClientObject client = clients.FirstOrDefault(c => c.Id == id);
        if (client != null)
        {
            clients.Remove(client);
            client.Close();
        }
    }

    protected internal async Task ListenAsync(CancellationToken cancellationToken)
    {
        try
        {
            tcpListener.Start();
            Console.WriteLine("Сервер запущен. Ожидание подключений...");
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient tcpClient = await tcpListener.AcceptTcpClientAsync();
                ClientObject clientObject = new ClientObject(tcpClient, this);
                clients.Add(clientObject);
                Task.Run(() => clientObject.ProcessAsync(cancellationToken));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
        finally
        {
            Disconnect();
        }
    }

    protected internal async Task BroadcastMessageAsync(string message, string id)
    {
        foreach (var client in clients)
        {
            if (client.Id != id)
            {
                await client.Writer.WriteLineAsync(message);
                await client.Writer.FlushAsync();
            }
        }
    }

    protected internal void Disconnect()
    {
        foreach (var client in clients)
        {
            client.Close();
        }
        tcpListener.Stop();
    }

    public void Stop()
    {
        cancellationTokenSource.Cancel();
    }
}

class ClientObject
{
    protected internal string Id { get; } = Guid.NewGuid().ToString();
    protected internal StreamWriter Writer { get; }
    protected internal StreamReader Reader { get; }
    private TcpClient client;
    private ServerObject server;

    public ClientObject(TcpClient tcpClient, ServerObject serverObject)
    {
        client = tcpClient;
        server = serverObject;
        var stream = client.GetStream();
        Reader = new StreamReader(stream);
        Writer = new StreamWriter(stream);
    }

    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        try
        {
            string userName = await Reader.ReadLineAsync();
            string message = $"{userName} вошел в чат";
            await server.BroadcastMessageAsync(message, Id);
            Console.WriteLine(message);
            while (!cancellationToken.IsCancellationRequested)
            {
                string receivedMessage = await Reader.ReadLineAsync();
                if (receivedMessage == null) continue;
                if (receivedMessage.ToLower() == "exit")
                {
                    message = $"{userName} покидает чат";
                    Console.WriteLine(message);
                    await server.BroadcastMessageAsync(message, Id);
                    break;
                }
                message = $"{userName}: {receivedMessage}";
                Console.WriteLine(message);
                await server.BroadcastMessageAsync(message, Id);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
        finally
        {
            server.RemoveConnection(Id);
        }
    }

    protected internal void Close()
    {
        Writer.Close();
        Reader.Close();
        client.Close();
    }
}

class Program
{
    static async Task Main(string[] args)
    {
        ServerObject server = new ServerObject();
        CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        await server.ListenAsync(cancellationTokenSource.Token);
    }
}