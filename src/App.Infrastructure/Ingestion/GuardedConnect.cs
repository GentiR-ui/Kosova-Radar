using System.Net;
using System.Net.Sockets;

namespace App.Infrastructure.Ingestion;

/// <summary>Thrown from the connect callback when the host resolves to a non-public address (REQ-020).</summary>
public sealed class BlockedAddressException() : Exception("The host resolves to a non-public address.");

/// <summary>
/// <see cref="SocketsHttpHandler.ConnectCallback"/> that resolves the host itself, refuses non-public addresses and
/// connects to the address it checked, so a DNS answer cannot change between check and connect (rebinding).
/// It runs for every connection, so redirects are covered too.
/// </summary>
public static class GuardedConnect
{
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, ct);
        }
        catch (SocketException e)
        {
            throw new HttpRequestException(HttpRequestError.NameResolutionError, "The host name cannot be resolved.", e);
        }

        // Any blocked answer refuses the host: a public site has no reason to publish a private address.
        if (addresses.Length == 0 || addresses.Any(a => !IpClassifier.IsPublic(a))) throw new BlockedAddressException();

        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException e)
            {
                socket.Dispose();
                last = e;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw new HttpRequestException(HttpRequestError.ConnectionError, "The host cannot be reached.", last);
    }
}
