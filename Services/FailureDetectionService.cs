using System;
using System.Collections.Generic;
using AcBoardSimulator.Api.Models;

namespace AcBoardSimulator.Api.Services
{
    public class FailureDetectionService : IFailureDetectionService
    {
        public DeviceFailureDto? EvaluateFailure(DeviceStateDto dto, AcTelemetryPayload payload)
        {
            var isPowered = payload.OperationalState.PowerState;
            var isCompressorRunning = isPowered && payload.Actuators.CompressorFrequencyHz > 0;
            double deltaT = dto.ReturnAirTemperature - dto.SupplyAirTemperature;

            // 1. Falha Elétrica: Falta de tensão ou subtensão crítica
            if (isPowered && payload.Electrical.AcInputVoltageV < 180.0)
            {
                return new DeviceFailureDto
                {
                    Code = "POWER_SUPPLY_FAULT",
                    Message = payload.Electrical.AcInputVoltageV == 0.0
                        ? "Falta de tensão na rede elétrica de alimentação"
                        : $"Subtensão crítica detectada na rede elétrica ({payload.Electrical.AcInputVoltageV:F1} V)",
                    Severity = "CRITICAL",
                    ProbableCause = "Queda de fase/disjuntor, oscilação severa da concessionária ou fusível rompido na placa",
                    TriggerSensors = new Dictionary<string, double>
                    {
                        { "ac_input_voltage_v", payload.Electrical.AcInputVoltageV },
                        { "total_current_a", payload.Electrical.TotalCurrentA }
                    }
                };
            }

            // 2. Pressão de Alta Crítica / Pressostato de Alta
            if (dto.LiquidLinePressure >= 600.0 || dto.CondensationAirTemperature >= 65.6)
            {
                return new DeviceFailureDto
                {
                    Code = "HIGH_PRESSURE_CUTOUT",
                    Message = "Pressão de descarga crítica ou sobreaquecimento de condensação (pressostato de alta)",
                    Severity = "CRITICAL",
                    ProbableCause = "Condensadora bloqueada/suja, motor do ventilador externo parado ou excesso de refrigerante",
                    TriggerSensors = new Dictionary<string, double>
                    {
                        { "liquid_line_pressure", dto.LiquidLinePressure },
                        { "condensation_air_temperature", dto.CondensationAirTemperature }
                    }
                };
            }

            // 3. Falta de Gás Refrigerante / Pressostato de Baixa
            if (dto.GasLinePressure < 50.0)
            {
                return new DeviceFailureDto
                {
                    Code = "LOW_GAS_PRESSURE",
                    Message = "Pressão da linha de sucção crítica (pressostato de baixa acionado)",
                    Severity = "CRITICAL",
                    ProbableCause = "Falta de fluido refrigerante por vazamento ou restrição severa na válvula de expansão",
                    TriggerSensors = new Dictionary<string, double>
                    {
                        { "gas_line_pressure", dto.GasLinePressure },
                        { "liquid_line_pressure", dto.LiquidLinePressure }
                    }
                };
            }

            // 4. Falha Mecânica do Compressor (compressor ligado mas pressões equalizadas e sem refrigeração)
            if (isCompressorRunning && Math.Abs(dto.LiquidLinePressure - dto.GasLinePressure) < 50.0 && deltaT < 4.0)
            {
                return new DeviceFailureDto
                {
                    Code = "COMPRESSOR_FAILURE",
                    Message = "Compressor inoperante ou sem compressão (pressões equalizadas)",
                    Severity = "CRITICAL",
                    ProbableCause = "Falha mecânica nas palhetas/válvulas do compressor ou inversor desarmado",
                    TriggerSensors = new Dictionary<string, double>
                    {
                        { "gas_line_pressure", dto.GasLinePressure },
                        { "liquid_line_pressure", dto.LiquidLinePressure },
                        { "supply_air_temperature", dto.SupplyAirTemperature },
                        { "return_air_temperature", dto.ReturnAirTemperature }
                    }
                };
            }

            // 5. Risco de Congelamento da Serpentina
            if (isPowered && (dto.SupplyAirTemperature < 11.7 || (dto.GasLinePressure < 100.0 && dto.EvaporatorCoilPressureDrop >= 80.0)))
            {
                return new DeviceFailureDto
                {
                    Code = "FREEZING_RISK",
                    Message = "Risco de congelamento na serpentina do evaporador",
                    Severity = "CRITICAL",
                    ProbableCause = "Temperatura de insuflamento excessivamente baixa devido a vazamento parcial de gás ou fluxo de ar reduzido",
                    TriggerSensors = new Dictionary<string, double>
                    {
                        { "supply_air_temperature", dto.SupplyAirTemperature },
                        { "gas_line_pressure", dto.GasLinePressure }
                    }
                };
            }

            // 6. Restrição do Fluxo de Ar no Evaporador / Filtro Muito Sujo
            if (isPowered && (dto.EvaporatorCoilPressureDrop >= 100.0 || (deltaT > 13.9 && isCompressorRunning)))
            {
                return new DeviceFailureDto
                {
                    Code = "AIR_RESTRICTION_EVAPORATOR",
                    Message = "Alta perda de carga no evaporador / fluxo de ar restrito",
                    Severity = "WARNING",
                    ProbableCause = "Filtro de ar muito sujo, aletas do evaporador obstruídas ou rotor da turbina lento",
                    TriggerSensors = new Dictionary<string, double>
                    {
                        { "evaporator_coil_pressure_drop", dto.EvaporatorCoilPressureDrop },
                        { "supply_air_temperature", dto.SupplyAirTemperature },
                        { "return_air_temperature", dto.ReturnAirTemperature }
                    }
                };
            }

            // 7. Obstrução na Serpentina Condensadora
            if (isPowered && (dto.CondenserCoilPressureDrop >= 40.0 || (dto.LiquidLinePressure > 445.0 && dto.CondensationAirTemperature > 55.0)))
            {
                return new DeviceFailureDto
                {
                    Code = "DIRTY_CONDENSER",
                    Message = "Bloqueio ou perda de carga elevada na condensadora",
                    Severity = "WARNING",
                    ProbableCause = "Serpentina externa obstruída por sujeira/folhas ou aletas amassadas",
                    TriggerSensors = new Dictionary<string, double>
                    {
                        { "condenser_coil_pressure_drop", dto.CondenserCoilPressureDrop },
                        { "liquid_line_pressure", dto.LiquidLinePressure },
                        { "condensation_air_temperature", dto.CondensationAirTemperature }
                    }
                };
            }

            // 8. Temperatura Externa Fora dos Limites
            if (dto.OutdoorTemperature < -15.0 || dto.OutdoorTemperature > 50.0)
            {
                return new DeviceFailureDto
                {
                    Code = "OUTDOOR_TEMPERATURE_OUT_OF_BOUNDS",
                    Message = $"Temperatura ambiente externa ({dto.OutdoorTemperature:F1} °C) fora do envelope operacional seguro (-15°C a 50°C)",
                    Severity = "FAULT",
                    ProbableCause = "Condição climática extrema externa incompatível com a capacidade segura do aparelho",
                    TriggerSensors = new Dictionary<string, double>
                    {
                        { "outdoor_temperature", dto.OutdoorTemperature }
                    }
                };
            }

            // 9. Temperatura de Retorno Fora da Faixa de Operação
            if (dto.ReturnAirTemperature < 16.0 || dto.ReturnAirTemperature > 32.0)
            {
                return new DeviceFailureDto
                {
                    Code = "RETURN_TEMPERATURE_OUT_OF_BOUNDS",
                    Message = $"Temperatura de retorno interno ({dto.ReturnAirTemperature:F1} °C) fora da faixa de operação recomendada (16°C a 32°C)",
                    Severity = "WARNING",
                    ProbableCause = "Carga térmica interna anormal ou ambiente com portas/janelas abertas em dia extremo",
                    TriggerSensors = new Dictionary<string, double>
                    {
                        { "return_air_temperature", dto.ReturnAirTemperature }
                    }
                };
            }

            return null;
        }
    }
}

