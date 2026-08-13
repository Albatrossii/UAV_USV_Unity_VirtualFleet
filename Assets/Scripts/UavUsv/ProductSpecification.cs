using UnityEngine;

namespace UavUsv
{
    /// <summary>
    /// Product-manual metadata attached to the runtime vehicle root.
    /// Dimensions are metres in Unity's 1 unit = 1 metre vehicle space.
    /// </summary>
    public sealed class ProductSpecification : MonoBehaviour
    {
        public string productModel;
        public Vector3 overallDimensionsMeters;
        public float massKilograms;
        public float payloadKilograms;
        public float maximumSpeedMetersPerSecond;
        public string endurance;
        [TextArea] public string installedEquipment;
        [TextArea] public string source;
    }
}
