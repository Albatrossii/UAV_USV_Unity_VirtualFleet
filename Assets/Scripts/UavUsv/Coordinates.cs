using UnityEngine;

namespace UavUsv
{
    public static class Coordinates
    {
        // The real operating area spans hundreds of metres while the vehicles
        // in the product manuals are only 1.2 / 1.5 metres wide or long.  Unity
        // is the presentation layer, so positions use a compact map scale
        // while every vehicle mesh remains true 1:1 metric size.
        public const float PresentationCoordinateScale = .18f;

        // Gazebo ENU (x, y, z-up) -> Unity (x, y-up, z).
        public static Vector3 ToUnity(float eastX, float northY, float upZ) => new Vector3(eastX, upZ, northY);
        public static Vector3 ToEnu(Vector3 unity) => new Vector3(unity.x, unity.z, unity.y);

        public static Vector3 ToPresentation(
            float eastX,
            float northY,
            float upZ)
        {
            return new Vector3(
                eastX * PresentationCoordinateScale,
                upZ * PresentationCoordinateScale,
                northY * PresentationCoordinateScale
            );
        }

        public static Vector3 ToPresentationEnvironment(
            float eastX,
            float northY,
            float upZ)
        {
            return ToUnity(eastX, northY, upZ) * PresentationCoordinateScale;
        }

        public static Vector3 PresentationToEnu(Vector3 unity)
        {
            return new Vector3(
                unity.x / PresentationCoordinateScale,
                unity.z / PresentationCoordinateScale,
                unity.y / PresentationCoordinateScale
            );
        }
    }
}
