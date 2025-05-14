using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class H3431DigitalTwinConsole : MonoBehaviour
{
    [Header("API Settings")]
    [Tooltip("Ваш API‑ключ OpenWeatherMap")]
    public string apiKey = "ВАШ_КЛЮЧ";
    [Tooltip("Город для запроса (например, Yekaterinburg,RU)")]
    public string city = "ВАШ_ГОРОД";
    [Tooltip("Интервал опроса внешнего API в секундах")]
    public float fetchInterval = 60f;

    [Header("Logging Settings")]
    [Tooltip("Интервал логирования в консоль (сек)")]
    public float logInterval = 5f;

    // Частота «считки» виртуального датчика: 30 Hz
    private const float sensorUpdateInterval = 1f / 30f;
    private float sensorTimer = 0f;

    // Внутренняя (истинная и измеренная) влажность
    private float trueHumidity;
    private float measuredHumidity;

    // Внешняя влажность из API
    private float externalHumidity;

    private float logTimer = 0f;

    // Спецификация датчика H3431 (диапазон и точность)
    private H3431Sensor sensor = new H3431Sensor();

    private void Start()
    {
        // Инициализация «истинной» и «измеренной» влажности серединой диапазона
        trueHumidity = (sensor.MinHumidity + sensor.MaxHumidity) / 2f;
        measuredHumidity = trueHumidity;

        // Запускаем корутину опроса внешнего API
        StartCoroutine(FetchExternalHumidityRoutine());
    }

    private void Update()
    {
        // Накопление времени для обновления датчика
        sensorTimer += Time.deltaTime;
        if (sensorTimer >= sensorUpdateInterval)
        {
            SimulateInternalHumidityWithError(sensorUpdateInterval);
            sensorTimer -= sensorUpdateInterval;
        }

        // Накопление времени для логирования
        logTimer += Time.deltaTime;
        if (logTimer >= logInterval)
        {
            LogToConsole();
            logTimer -= logInterval;
        }
    }

    /// <summary>
    /// Симулирует «истинную» влажность с дрейфом до ±0.5% RH/с и добавляет измерительную погрешность ±2.5% RH.
    /// </summary>
    /// <param name="dt">Интервал времени с последнего обновления (сек).</param>
    private void SimulateInternalHumidityWithError(float dt)
    {
        // Максимальный дрейф в процентах влажности в секунду
        float maxDriftPerSecond = 0.5f;

        // 1) Генерируем «истинную» влажность с дрейфом
        float drift = UnityEngine.Random.Range(-maxDriftPerSecond, maxDriftPerSecond) * dt;
        trueHumidity = Mathf.Clamp(trueHumidity + drift, sensor.MinHumidity, sensor.MaxHumidity);

        // 2) Добавляем измерительную погрешность
        float error = UnityEngine.Random.Range(-sensor.HumidityAccuracy, sensor.HumidityAccuracy);
        measuredHumidity = Mathf.Clamp(trueHumidity + error, sensor.MinHumidity, sensor.MaxHumidity);
    }

    /// <summary>
    /// Корутина для периодического опроса OpenWeatherMap API.
    /// </summary>
    private IEnumerator FetchExternalHumidityRoutine()
    {
        while (true)
        {
            string url = $"https://api.openweathermap.org/data/2.5/weather?q={city}&appid={apiKey}&units=metric";
            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                yield return www.SendWebRequest();
                if (www.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var data = JsonUtility.FromJson<OpenWeatherResponse>(www.downloadHandler.text);
                        externalHumidity = data.main.humidity;
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[H3431Twin] JSON parse error: {e.Message}");
                    }
                }
                else
                {
                    Debug.LogError($"[H3431Twin] HTTP error: {www.error}");
                }
            }
            yield return new WaitForSeconds(fetchInterval);
        }
    }

    /// <summary>
    /// Выводит в консоль текущие значения внутренней и внешней влажности.
    /// </summary>
    private void LogToConsole()
    {
        Debug.Log(
            $"[H3431Twin] Внутр. влажность (изм.): {measuredHumidity:F1}% " +
            $"(true: {trueHumidity:F1}%, ±{sensor.HumidityAccuracy}%RH)\n" +
            $"[H3431Twin] Внешняя влажность: {externalHumidity:F1}%"
        );
    }

    #region Вспомогательные классы

    /// <summary>
    /// Характеристики датчика H3431 (диапазон и точность).
    /// </summary>
    public class H3431Sensor
    {
        public readonly float MinHumidity = 0f;
        public readonly float MaxHumidity = 100f;
        public readonly float HumidityAccuracy = 2.5f;  // ±2.5% RH
    }

    /// <summary>
    /// Классы для разбора JSON-ответа OpenWeatherMap.
    /// </summary>
    [Serializable]
    private class OpenWeatherResponse
    {
        public MainData main;
        [Serializable]
        public class MainData
        {
            public float humidity;
        }
    }

    #endregion
}
