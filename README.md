# 🧊 API de Integração: Telemetria AC Inverter (Gêmeo Digital)

Bem-vindo à documentação oficial de integração do **Gêmeo Digital AC Inverter**. 

Este documento descreve a estrutura de dados (Payload) gerada pelas placas eletrônicas virtuais. O sistema foi desenhado para simular o comportamento de centrais de ar-condicionado em tempo real, permitindo testes de carga, desenvolvimento de dashboards e validação de regras de manutenção preditiva.

---

## 📡 1. Visão Geral da Arquitetura (Webhook)

O sistema opera no modelo **Push (Webhook)**. A placa eletrônica atua como um cliente HTTP, enviando ativamente os dados em tempo real para o endpoint da sua API de Manutenção em intervalos configuráveis (ex: a cada 3 segundos).

* **Protocolo:** HTTP/1.1 ou HTTP/2
* **Método Esperado no seu Servidor:** `POST`
* **Formato do Corpo:** `application/json`

---

## 🔐 2. Autenticação e Cabeçalhos

Para garantir que seu sistema receba dados apenas de placas autorizadas da nossa frota, o simulador enviará um cabeçalho customizado em cada requisição. O seu endpoint (receptor) deve validar esta chave:

| Cabeçalho | Valor Exemplo | Descrição |
| :--- | :--- | :--- |
| `Content-Type` | `application/json` | Formato do payload enviado. |
| `X-Device-Api-Key` | `sua-chave-secreta-123` | Chave estática configurada na placa para validar a origem dos dados. |

---

## 📦 3. Exemplo de Payload (JSON)

Abaixo está o corpo completo da requisição que o seu servidor receberá a cada ciclo de leitura da placa:

```json
{
  "metadata": {
    "deviceId": "AC-INV-MARILIA-001",
    "serialNumber": "SN2026098841BR",
    "boardModel": "INV-CTRL-V4.2",
    "firmwareVersion": "2.4.1",
    "refrigerantType": "R410A",
    "timestampUtc": "2026-11-02T14:30:00Z",
    "simulationMode": "MANUAL_OVERRIDE"
  },
  "operationalState": {
    "powerState": true,
    "operationMode": "COOL",
    "fanSpeedSetting": "HIGH",
    "swingEnabled": true,
    "uptimeSeconds": 18450,
    "compressorRuntimeHours": 1420.5
  },
  "temperatures": {
    "setpointC": 21.0,
    "indoorReturnAirC": 23.4,
    "evaporatorCoilC": 8.2,
    "outdoorAmbientC": 32.5,
    "condenserCoilC": 44.8,
    "compressorDischargeC": 78.6,
    "ipmModuleC": 54.1
  },
  "refrigerationCycle": {
    "suctionPressurePsi": 124.5,
    "dischargePressurePsi": 368.0,
    "eevOpeningSteps": 240,
    "superheatC": 6.4,
    "subcoolingC": 5.2
  },
  "electrical": {
    "acInputVoltageV": 221.3,
    "dcBusVoltageV": 312.8,
    "totalCurrentA": 5.85,
    "compressorCurrentA": 5.20,
    "activePowerW": 1268.4,
    "powerFactor": 0.98
  },
  "actuators": {
    "compressorFrequencyHz": 58.0,
    "indoorFanRpm": 1050,
    "outdoorFanRpm": 720,
    "fourWayValveActive": false
  },
  "diagnostics": {
    "errorCode": "NONE",
    "errorDescription": "Operação normal",
    "protectionTripped": false,
    "interBoardCommOk": true,
    "airFilterDirty": false
  }
}
```

---

## 📖 4. Dicionário de Dados

O payload é dividido em 6 blocos lógicos. Todos os nomes de propriedades seguem o padrão *camelCase*.

### 4.1. `metadata` (Identificação)
Dados imutáveis ou de rastreabilidade do equipamento.

| Propriedade | Tipo | Descrição |
| :--- | :--- | :--- |
| `deviceId` | `string` | **ID único do equipamento.** Use como Chave Estrangeira (FK) no seu banco de dados. |
| `serialNumber` | `string` | Número de série físico da condensadora. |
| `boardModel` | `string` | Versão de hardware da placa Inverter. |
| `refrigerantType` | `string` | Tipo de gás utilizado (ex: `R410A`). Essencial para cálculos termodinâmicos. |
| `timestampUtc` | `datetime` | Data e hora exata da leitura na placa (Fuso UTC 0, ISO 8601). |

### 4.2. `operationalState` (Estado Geral)
| Propriedade | Tipo | Descrição |
| :--- | :--- | :--- |
| `powerState` | `boolean` | `true` (Ligado) ou `false` (Desligado em Stand-by). |
| `operationMode` | `string` | Modo atual selecionado: `COOL`, `HEAT`, `FAN`, `DRY` ou `AUTO`. |
| `fanSpeedSetting` | `string` | Velocidade programada do vento: `LOW`, `MEDIUM`, `HIGH`, `TURBO`, `AUTO`. |
| `uptimeSeconds` | `integer` | Tempo contínuo em segundos desde que a placa foi ligada. |
| `compressorRuntimeHours`| `double` | Odômetro do compressor (horas acumuladas). Base para manutenção preventiva. |

### 4.3. `temperatures` (Sensores Térmicos em °C)
*Nota: Todas as unidades estão em Graus Celsius (°C).*

| Propriedade | Tipo | Descrição Prática |
| :--- | :--- | :--- |
| `setpointC` | `double` | Temperatura alvo desejada pelo usuário no controle remoto. |
| `indoorReturnAirC` | `double` | (T1) Temperatura real do ar do ambiente interno sendo sugado pela máquina. |
| `evaporatorCoilC` | `double` | (T2) Temperatura da serpentina interna. Valores < `0°C` indicam congelamento. |
| `outdoorAmbientC` | `double` | (T4) Temperatura do ar no ambiente externo. |
| `condenserCoilC` | `double` | (T3) Temperatura da serpentina externa. Valores > `60°C` indicam bloqueio de ventilação. |
| `compressorDischargeC`| `double` | (T5) Temperatura no topo do compressor. **Crítico:** Acima de `115°C` a placa desarma. |
| `ipmModuleC` | `double` | Temperatura do dissipador do módulo eletrônico de potência (IPM). |

### 4.4. `refrigerationCycle` (Física dos Gases)
| Propriedade | Tipo | Unidade | Descrição |
| :--- | :--- | :--- | :--- |
| `suctionPressurePsi` | `double` | PSI | Pressão da linha de sucção (Baixa). Normal para R410A: ~120 PSI. |
| `dischargePressurePsi`| `double` | PSI | Pressão da linha de descarga (Alta). Normal para R410A: ~350 PSI. |
| `eevOpeningSteps` | `integer`| Passos | Nível de abertura da Válvula de Expansão Eletrônica (0 a 480). |
| `superheatC` | `double` | °C | Superaquecimento (Crucial para diagnóstico de carga de gás). |
| `subcoolingC` | `double` | °C | Sub-resfriamento. |

### 4.5. `electrical` (Grandezas Elétricas)
| Propriedade | Tipo | Unidade | Descrição |
| :--- | :--- | :--- | :--- |
| `acInputVoltageV` | `double` | Volts (V) | Tensão da rede elétrica. Desarmes ocorrem em quedas (< 180V). |
| `dcBusVoltageV` | `double` | Volts (V) | Tensão contínua do barramento interno da placa (~Tensão AC * 1.41). |
| `totalCurrentA` | `double` | Amperes (A)| Consumo total de corrente do equipamento no instante da leitura. |
| `activePowerW` | `double` | Watts (W) | Potência real calculada para auditoria de consumo energético. |

### 4.6. `actuators` (Motores e Atuadores)
| Propriedade | Tipo | Unidade | Descrição |
| :--- | :--- | :--- | :--- |
| `compressorFrequencyHz`| `double` | Hertz (Hz)| Velocidade do compressor Inverter (0 Hz a 120 Hz). |
| `indoorFanRpm` | `integer`| RPM | Rotação por minuto do ventilador da unidade interna (Evaporadora). |
| `outdoorFanRpm` | `integer`| RPM | Rotação por minuto do ventilador da unidade externa (Condensadora). |

### 4.7. `diagnostics` (Central de Falhas)
Se a variável `errorCode` for diferente de `"NONE"`, recomendamos a criação de um chamado de manutenção automatizado no sistema receptor.

| Propriedade | Tipo | Descrição |
| :--- | :--- | :--- |
| `protectionTripped` | `boolean` | Se `true`, a placa forçou o desligamento do motor para evitar danos graves. |
| `errorCode` | `string` | O código de erro idêntico ao exibido no display digital do ar-condicionado. |
| `errorDescription` | `string` | Texto amigável descrevendo a falha. |
| `airFilterDirty` | `boolean` | Se `true`, notificar usuário para limpeza do filtro da evaporadora. |

---

## 🛠️ 5. Tabela de Mapeamento de Erros (Troubleshooting)

Use esta tabela para configurar gatilhos e alertas de chamados automáticos no seu sistema:

| Código | Falha Física Detectada | Ação Recomendada pelo Sistema |
| :---: | :--- | :--- |
| **NONE** | Operação Normal | Manter status verde. Registrar histórico. |
| **EC** | Vazamento de Gás Fluido Refrigerante | Abrir chamado de Manutenção Corretiva (Caça-vazamento e Recarga). |
| **P4** | Desarme por Pressão/Temperatura Alta | Alertar bloqueio na condensadora (Ventilador travado ou sujeira extrema). |
| **P1** | Subtensão na Rede Elétrica | Alertar equipe de Engenharia Elétrica predial. (Falha externa à máquina). |
| **E1** | Erro de Comunicação Serial (Evap/Cond) | Despachar técnico para revisar fiação de interligação entre unidades. |

---
*Gerado para Integração de Gêmeos Digitais IoT.*