"use client"

import * as React from "react"
import { Dialog as DialogPrimitive } from "@base-ui/react/dialog"

import { cn } from "@/lib/utils"
import { DialogBackdrop, DialogPortal } from "@/components/ui/dialog"

/** Panel lateral que entra desde la derecha (menú del back-office en pantallas chicas). Reusa el
 * Dialog de base-ui —foco atrapado, Escape, click afuera— sin sumar dependencias. */
const Drawer = DialogPrimitive.Root
const DrawerClose = DialogPrimitive.Close

function DrawerContent({ className, children, ...props }: DialogPrimitive.Popup.Props) {
  return (
    <DialogPortal>
      <DialogBackdrop />
      <DialogPrimitive.Popup
        data-slot="drawer-content"
        className={cn(
          "fixed inset-y-0 right-0 z-50 flex w-[min(20rem,88vw)] flex-col gap-1 overflow-y-auto rounded-l-2xl border-l bg-card p-4 pb-[max(1rem,env(safe-area-inset-bottom))] text-card-foreground shadow-xl outline-none duration-200 data-open:animate-in data-open:slide-in-from-right data-closed:animate-out data-closed:slide-out-to-right",
          className
        )}
        {...props}
      >
        {children}
      </DialogPrimitive.Popup>
    </DialogPortal>
  )
}

function DrawerTitle({ className, ...props }: DialogPrimitive.Title.Props) {
  return (
    <DialogPrimitive.Title
      data-slot="drawer-title"
      className={cn("font-display text-lg font-bold text-bf-azul", className)}
      {...props}
    />
  )
}

export { Drawer, DrawerClose, DrawerContent, DrawerTitle }
