"use client";

import { X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

/**
 * A list of single line values. Used for authors and tags, the two lists whose
 * items carry a rule worth showing per item. The prose lists in the install
 * section are a plain textarea instead, one item per line, which suits multi
 * sentence steps far better than a stack of inputs.
 */
export function StringList({
  id,
  label,
  values,
  onChange,
  placeholder,
  hint,
  errors,
  addLabel,
}: {
  id: string;
  label: string;
  values: string[];
  onChange: (values: string[]) => void;
  placeholder?: string;
  hint?: string;
  /** Keyed by index, matching the dotted paths validate() produces. */
  errors?: Record<number, string | undefined>;
  addLabel: string;
}) {
  const set = (index: number, value: string) =>
    onChange(values.map((entry, at) => (at === index ? value : entry)));

  return (
    <div className="space-y-2">
      <Label htmlFor={`${id}.0`}>{label}</Label>
      {hint && (
        <p id={`${id}-hint`} className="text-muted-foreground text-sm">
          {hint}
        </p>
      )}

      <div className="space-y-2">
        {values.map((value, index) => {
          const error = errors?.[index];
          return (
            <div key={index} className="space-y-1">
              <div className="flex gap-2">
                <Input
                  id={`${id}.${index}`}
                  value={value}
                  placeholder={placeholder}
                  aria-invalid={Boolean(error)}
                  aria-describedby={
                    error
                      ? `${id}.${index}-error`
                      : hint
                        ? `${id}-hint`
                        : undefined
                  }
                  onChange={(event) => set(index, event.target.value)}
                />
                {values.length > 1 && (
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    aria-label={`Remove ${label.toLowerCase()} ${index + 1}`}
                    onClick={() =>
                      onChange(values.filter((_, at) => at !== index))
                    }
                  >
                    <X />
                  </Button>
                )}
              </div>
              {error && (
                <p
                  id={`${id}.${index}-error`}
                  role="alert"
                  className="text-destructive text-sm"
                >
                  {error}
                </p>
              )}
            </div>
          );
        })}
      </div>

      <Button
        type="button"
        variant="outline"
        size="sm"
        onClick={() => onChange([...values, ""])}
      >
        {addLabel}
      </Button>
    </div>
  );
}
