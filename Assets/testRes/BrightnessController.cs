using UnityEngine;

[ExecuteInEditMode]
public class BrightnessController : MonoBehaviour
{
    public AnimationCurve brightnessCurve = AnimationCurve.Linear(0, 0, 1, 1);
    public Texture2D curveTexture;
    public Material targetMaterial;

    void OnValidate() // 当数值变化时自动执行
    {
        UpdateCurveTexture();
    }

    void UpdateCurveTexture()
    {
        if (curveTexture == null)
        {
            curveTexture = new Texture2D(256, 1, TextureFormat.RFloat, false);
            curveTexture.wrapMode = TextureWrapMode.Clamp;
        }

        // 将曲线数据写入纹理
        for (int x = 0; x < 256; x++)
        {
            float t = x / 255f;
            float value = brightnessCurve.Evaluate(t);
            curveTexture.SetPixel(x, 0, new Color(value, 0, 0));
        }
        curveTexture.Apply();

        // 传递给材质
        if (targetMaterial == null)
            targetMaterial = GetComponent<Renderer>().sharedMaterial;

        targetMaterial.SetTexture("_CurveTex", curveTexture);
    }
}
