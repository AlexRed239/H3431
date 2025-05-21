using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public class H3431DigitalTwinPhysical : MonoBehaviour
{
    [Header("API Settings")]
    [Tooltip("Ваш API-ключ OpenWeatherMap")]
    public string apiKey = "ВАШ_КЛЮЧ";
    [Tooltip("Город (например Yekaterinburg,RU)")]
    public string city = "Yekaterinburg,RU";
    [Tooltip("Интервал опроса API (сек)")]
    public float fetchInterval = 60f;

    // Ваша модель емкостного датчика
    private CapacitiveHumiditySensor sensor = new CapacitiveHumiditySensor();

    private void Start()
    {
        StartCoroutine(FetchAndSimulateRoutine());
    }

    private IEnumerator FetchAndSimulateRoutine()
    {
        while (true)
        {
            // 1) Запрос к API
            string url = $"https://api.openweathermap.org/data/2.5/weather?q={city}&appid={apiKey}&units=metric";
            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                yield return www.SendWebRequest();
                if (www.result == UnityWebRequest.Result.Success)
                {
                    // 2) Разбор JSON
                    var data = JsonUtility.FromJson<OWResponse>(www.downloadHandler.text);
                    double tempC = data.main.temp;
                    double rhPercent = data.main.humidity;

                    // 3) Пересчёт в абсолютную влажность ρ (г/м³)
                    double absHum = CalculateAbsoluteHumidity(rhPercent, tempC);

                    // 4) Симуляция сенсора
                    var result = sensor.SimulateSensor(absHum, tempC);

                    // 5) Лог в консоль
                    Debug.Log(
                        // Блок 1: исходные данные из API
                        $"[H3431Physical] 1) Температура (API): {tempC:F1} °C\n" +
                        $"[H3431Physical] 2) Относительная влажность (API): {rhPercent:F1} %\n" +
                        // Блок 2: вычисленная абсолютная влажность
                        $"[H3431Physical] 3) Абсолютная влажность: {absHum:F2} г/м³\n" +
                        // Блок 3: модель сенсора
                        $"[H3431Physical] 4) Относительная влажность (модел.): {result.RH:F1} %\n" +
                        $"[H3431Physical] 5) Ёмкость сенсора: {result.Capacitance:E2} Ф\n" +
                        $"[H3431Physical] 6) Точка росы: {result.DewPoint:F1} °C\n" +
                        $"[H3431Physical] 7) Удельная энтальпия: {result.Enthalpy:F1} кДж/кг"
                    );

                }
                else
                {
                    Debug.LogError($"[H3431Physical] HTTP error: {www.error}");
                }
            }

            yield return new WaitForSeconds(fetchInterval);
        }
    }

    // Рассчёт абсолютной влажности ρ из RH и T
    // ρ = 1000 * p_v / (Rv * T_kelvin), где p_v = (RH/100)*e_s(T)
    private double CalculateAbsoluteHumidity(double rhPercent, double tempC)
    {
        const double Rv = 461.5; // Дж/(кг·K)
        double Tkelvin = tempC + 273.15;
        // e_s в Па (Magnus)
        double e_s = 611.2 * Math.Exp(17.62 * tempC / (243.12 + tempC));
        double p_v = (rhPercent / 100.0) * e_s;
        // ρ в кг/м³, умножаем на 1000 → г/м³
        return 1000.0 * p_v / (Rv * Tkelvin);
    }

    // Классы для парсинга OpenWeatherMap
    [Serializable]
    private class OWResponse
    {
        public Main main;
        [Serializable]
        public class Main
        {
            public double temp;
            public double humidity;
        }
    }

    // ---------------------------------------------------
    // Физическая модель емкостного сенсора
    // ---------------------------------------------------
    public class CapacitiveHumiditySensor
    {
        // Константы
        private const double eps0 = 8.854e-12;   // Ф/м
        private const double Rv = 461.5;       // Дж/(кг·K)
        private const double epsDry = 4.0;         // ε_r при 0% RH
        private const double epsWet = 8.0;         // ε_r при 100% RH
        private const double plateArea = 10e-6;       // м² (пример 10 мм²)
        private const double thickness = 5e-6;        // м (пример 5 мкм)
        private double lastRH = 0.0;

        // Результаты симуляции
        public struct Result
        {
            public double RH;
            public double Capacitance;
            public double DewPoint;
            public double Enthalpy;
        }

        // Основной метод: на входе ρ (г/м³) и T (°C)
        public Result SimulateSensor(double absHum, double tempC)
        {
            // 1. Относительная влажность (теоретическая)
            double RH = CalculateRelativeHumidity(absHum, tempC);

            // 2. Погрешность ±2.5% RH
            RH *= 1.0 + (UnityEngine.Random.Range(-2.5f, 2.5f) / 100.0);

            // 3. Гистерезис ±1% в зависимости от направления
            if (RH > lastRH) RH += 1.0; else RH -= 1.0;
            lastRH = RH;

            RH = Math.Max(0, Math.Min(100, RH));

            // 4. Диэлектрическая проницаемость
            double eps_r = epsDry + (epsWet - epsDry) * (RH / 100.0);

            // 5. Ёмкость
            double C = eps0 * eps_r * (plateArea / thickness);

            // 6. Точка росы
            double Td = CalculateDewPoint(tempC, RH);

            // 7. Удельная энтальпия (кДж/кг сухого воздуха)
            double h = CalculateEnthalpy(tempC, absHum);

            return new Result
            {
                RH = RH,
                Capacitance = C,
                DewPoint = Td,
                Enthalpy = h
            };
        }

        // Относительная влажность по входной ρ (г/м³) и T (°C)
        private double CalculateRelativeHumidity(double absHum, double tempC)
        {
            double T = tempC + 273.15;
            // Парциальное давление воды
            double p_v = (absHum / 1000.0) * Rv * T;
            // Насыщенное давление (Па)
            double e_s = 611.2 * Math.Exp(17.62 * tempC / (243.12 + tempC));
            return (p_v / e_s) * 100.0;
        }

        // Точка росы (°C)
        private double CalculateDewPoint(double tempC, double RH)
        {
            double gamma = Math.Log(RH / 100.0) + (17.62 * tempC) / (243.12 + tempC);
            return (243.12 * gamma) / (17.62 - gamma);
        }

        // Удельная энтальпия (кДж/кг сухого воздуха)
        private double CalculateEnthalpy(double tempC, double absHum)
        {
            double T = tempC + 273.15;
            double p_v = (absHum / 1000.0) * Rv * T;
            double P_total = 101325.0;
            double x = 0.622 * p_v / (P_total - p_v);
            // 1.006*T + x*(2501 + 1.86*T) [кДж/кг]
            return 1.006 * tempC + x * (2501.0 + 1.86 * tempC);
        }
    }
}
