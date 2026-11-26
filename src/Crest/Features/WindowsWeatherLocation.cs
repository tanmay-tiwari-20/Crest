using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;

namespace Crest.Features;

internal static class WindowsWeatherLocation
{
    public static async Task<(double Latitude, double Longitude)?> ReadAsync(CancellationToken cancellationToken)
    {
        var access = await Geolocator.RequestAccessAsync().AsTask(cancellationToken);
        if (access != GeolocationAccessStatus.Allowed) return null;

        cancellationToken.ThrowIfCancellationRequested();
        var locator = new Geolocator { DesiredAccuracy = PositionAccuracy.Default };
        var position = await locator.GetGeopositionAsync(TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(10)).AsTask(cancellationToken);
        var coordinates = position.Coordinate.Point.Position;
        return (coordinates.Latitude, coordinates.Longitude);
    }
}
