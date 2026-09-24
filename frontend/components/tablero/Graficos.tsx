"use client";

import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  LabelList,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";

/** Lo que el tooltip necesita de recharts: se tipa a mano porque la firma genérica de `content`
 * (ValueType/NameType) no acepta un componente de valores numéricos. */
interface PropsGlobo {
  active?: boolean;
  label?: unknown;
  payload?: ReadonlyArray<{ value?: unknown; payload?: unknown }>;
}

/**
 * Gráficos del tablero (B2, docs/diseño_b2_tablero.md), según la skill de visualización del proyecto:
 * una sola serie por gráfico (sin leyenda: el título la nombra), barras de hasta 24 px con 4 px
 * redondeados en el extremo del dato y rectos en la base, grilla hairline sin punteado, texto en tintas
 * de texto (nunca del color de la serie) y tooltip en cada barra. Colores desde --serie-principal y
 * --serie-negativa (globals.css), validados en claro y oscuro. Cada gráfico trae su tabla: el tooltip
 * complementa, nunca es el único acceso al valor.
 */

/** El signo va delante del símbolo: "−$3.400", no "$-3.400". */
const signo = (n: number) => (n < 0 ? "−" : "");

export const pesos = (n: number) =>
  `${signo(n)}$${Math.abs(n).toLocaleString("es-AR", { maximumFractionDigits: 0 })}`;

const pesosCortos = (n: number, decimales = 1) => {
  const a = Math.abs(n);
  const cuerpo =
    a >= 1_000_000
      ? `${(a / 1_000_000).toLocaleString("es-AR", { maximumFractionDigits: decimales })}M`
      : a >= 1_000
        ? `${(a / 1_000).toLocaleString("es-AR", { maximumFractionDigits: decimales })}k`
        : a.toLocaleString("es-AR", { maximumFractionDigits: 0 });
  return `${signo(n)}$${cuerpo}`;
};

/** Ticks del eje: números redondos, sin decimales, que entren en una línea. */
const tickPesos = (n: number) => pesosCortos(n, 0);

const diaCorto = (iso: string) => {
  const [, m, d] = iso.split("-");
  return `${d}/${m}`;
};

const EJE = { fontSize: 12, fill: "var(--muted-foreground)" };

/** Valor primero y fuerte, etiqueta después; key de línea con el color de la marca. */
function Globo({ active, payload, label, formato, titulo }: PropsGlobo & {
  formato: (v: number) => string;
  titulo?: (l: string) => string;
}) {
  if (!active || !payload?.length || payload[0].value == null) return null;
  const valor = Number(payload[0].value);
  const color = (payload[0].payload as { color?: string }).color ?? "var(--serie-principal)";
  return (
    <div className="rounded-lg border bg-popover px-3 py-2 text-sm shadow-md">
      <p className="font-semibold text-foreground">{formato(valor)}</p>
      <p className="flex items-center gap-2 text-muted-foreground">
        <span aria-hidden className="inline-block h-0.5 w-3 rounded-full" style={{ background: color }} />
        {titulo ? titulo(String(label)) : String(label)}
      </p>
    </div>
  );
}

function TablaDatos({ columnas, filas }: { columnas: [string, string]; filas: [string, string][] }) {
  return (
    <details className="mt-2 text-sm">
      <summary className="cursor-pointer text-muted-foreground">Ver tabla</summary>
      <table className="mt-2 w-full tabular-nums">
        <thead>
          <tr className="text-left text-muted-foreground">
            <th className="py-1 font-normal">{columnas[0]}</th>
            <th className="py-1 text-right font-normal">{columnas[1]}</th>
          </tr>
        </thead>
        <tbody>
          {filas.map(([a, b]) => (
            <tr key={a} className="border-t">
              <td className="py-1">{a}</td>
              <td className="py-1 text-right">{b}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </details>
  );
}

/** Entregas por día: columnas desde una sola base. */
export function GraficoEntregas({ datos }: { datos: { fecha: string; entregas: number }[] }) {
  return (
    <div>
      <div className="h-56 w-full">
        <ResponsiveContainer>
          <BarChart data={datos} margin={{ top: 8, right: 8, left: -16, bottom: 0 }} barCategoryGap={2}>
            <CartesianGrid vertical={false} stroke="var(--grilla-grafico)" strokeDasharray="0" />
            <XAxis dataKey="fecha" tickFormatter={diaCorto} tick={EJE} tickLine={false} axisLine={{ stroke: "var(--grilla-grafico)" }} minTickGap={16} />
            <YAxis allowDecimals={false} tick={EJE} tickLine={false} axisLine={false} />
            <Tooltip
              cursor={{ fill: "var(--grilla-grafico)", opacity: 0.6 }}
              content={(p) => <Globo {...p} formato={(v) => `${v} entrega${v === 1 ? "" : "s"}`} titulo={diaCorto} />}
            />
            <Bar dataKey="entregas" fill="var(--serie-principal)" radius={[4, 4, 0, 0]} maxBarSize={24} isAnimationActive={false} />
          </BarChart>
        </ResponsiveContainer>
      </div>
      <TablaDatos
        columnas={["Día", "Entregas"]}
        filas={datos.filter((d) => d.entregas > 0).map((d) => [diaCorto(d.fecha), String(d.entregas)])}
      />
    </div>
  );
}

/** Margen por día: sobre una línea base en 0; positivo en la serie principal, negativo en naranja y hacia abajo. */
export function GraficoMargen({ datos }: { datos: { fecha: string; margen: number | null }[] }) {
  const conColor = datos.map((d) => ({
    ...d,
    color: d.margen !== null && d.margen < 0 ? "var(--serie-negativa)" : "var(--serie-principal)",
  }));
  return (
    <div>
      <div className="h-56 w-full">
        <ResponsiveContainer>
          <BarChart data={conColor} margin={{ top: 8, right: 8, left: 8, bottom: 0 }} barCategoryGap={2}>
            <CartesianGrid vertical={false} stroke="var(--grilla-grafico)" strokeDasharray="0" />
            <XAxis dataKey="fecha" tickFormatter={diaCorto} tick={EJE} tickLine={false} axisLine={false} minTickGap={16} />
            <YAxis tickFormatter={tickPesos} tick={EJE} tickLine={false} axisLine={false} width={56} allowDecimals={false} />
            <ReferenceLine y={0} stroke="var(--muted-foreground)" strokeWidth={1} />
            <Tooltip
              cursor={{ fill: "var(--grilla-grafico)", opacity: 0.6 }}
              content={(p) => <Globo {...p} formato={pesos} titulo={diaCorto} />}
            />
            <Bar dataKey="margen" maxBarSize={24} isAnimationActive={false}>
              {conColor.map((d) => (
                <Cell
                  key={d.fecha}
                  fill={d.color}
                  // El extremo del dato va redondeado: arriba si es positivo, abajo si es negativo.
                  radius={(d.margen !== null && d.margen < 0 ? [0, 0, 4, 4] : [4, 4, 0, 0]) as unknown as number}
                />
              ))}
            </Bar>
          </BarChart>
        </ResponsiveContainer>
      </div>
      <TablaDatos
        columnas={["Día", "Margen"]}
        filas={datos.filter((d) => d.margen !== null).map((d) => [diaCorto(d.fecha), pesos(d.margen!)])}
      />
    </div>
  );
}

/** Magnitud por categoría: barras horizontales (nombres largos), valor en la punta. */
export function BarrasHorizontales({ datos, columna }: { datos: { nombre: string; valor: number }[]; columna: string }) {
  const alto = Math.max(datos.length * 36 + 16, 80);
  return (
    <div>
      <div className="w-full" style={{ height: alto }}>
        <ResponsiveContainer>
          <BarChart data={datos} layout="vertical" margin={{ top: 0, right: 72, left: 0, bottom: 0 }} barCategoryGap={2}>
            <XAxis type="number" hide />
            <YAxis type="category" dataKey="nombre" width={130} tick={EJE} tickLine={false} axisLine={false} interval={0} />
            <Tooltip cursor={{ fill: "var(--grilla-grafico)", opacity: 0.6 }} content={(p) => <Globo {...p} formato={pesos} />} />
            <Bar dataKey="valor" fill="var(--serie-principal)" radius={[0, 4, 4, 0]} maxBarSize={24} isAnimationActive={false}>
              <LabelList dataKey="valor" position="right" formatter={(v) => pesosCortos(Number(v))} style={{ fontSize: 12, fill: "var(--foreground)" }} />
            </Bar>
          </BarChart>
        </ResponsiveContainer>
      </div>
      <TablaDatos columnas={[columna, "Facturación"]} filas={datos.map((d) => [d.nombre, pesos(d.valor)])} />
    </div>
  );
}
