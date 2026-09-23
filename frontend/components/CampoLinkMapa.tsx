import { MapPin } from "lucide-react";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

interface CampoLinkMapaProps {
  id: string;
  value: string;
  onChange: (valor: string) => void;
  disabled?: boolean;
  /** El servidor tomó el punto del link (la ubicación quedó con confianza alta). */
  aplicado?: boolean;
  /** Motivo por el que el servidor NO usó el link y ubicó por la dirección. */
  error?: string | null;
  /** Se está leyendo la dirección del link. */
  leyendo?: boolean;
}

/** Link de Google Maps opcional para fijar el punto exacto de la dirección de entrega. Compartido
 * entre el selector de dirección del portal y el alta interna de pedidos. */
export function CampoLinkMapa({ id, value, onChange, disabled, aplicado, error, leyendo }: CampoLinkMapaProps) {
  return (
    <div className="flex flex-col gap-2">
      <Label htmlFor={id}>Link de Google Maps (opcional)</Label>
      <Input
        id={id}
        type="text"
        inputMode="url"
        autoComplete="off"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder="https://maps.app.goo.gl/…"
        disabled={disabled}
      />
      {leyendo ? (
        <p className="text-sm text-muted-foreground">Leyendo la dirección del link…</p>
      ) : aplicado && value.trim() !== "" ? (
        <p className="flex items-center gap-1 text-sm text-green-700">
          <MapPin className="size-4" />
          Punto fijado desde el link de Maps.
        </p>
      ) : error ? (
        <p className="text-sm text-amber-600">No usamos el link: {error} Se ubicó por la dirección.</p>
      ) : (
        <p className="text-xs text-muted-foreground">
          En Google Maps tocá el lugar → <b>Compartir</b> → copiá el link y pegalo acá para fijar el punto exacto.
        </p>
      )}
    </div>
  );
}
