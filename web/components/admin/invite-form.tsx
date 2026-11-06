"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { Send } from "lucide-react";
import { Controller, useForm } from "react-hook-form";
import { toast } from "sonner";

import { useInvitationMutations } from "@/components/admin/use-invitations";
import { Button } from "@/components/ui/button";
import {
  Field,
  FieldDescription,
  FieldError,
  FieldLabel,
} from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { inviteSchema, type InviteValues } from "@/lib/invitations";

/**
 * Invites a colleague by email. On success the form clears and a toast confirms; an API error (the email already has
 * an account, Keycloak failing) shows as a toast.
 * @returns the invite form
 */
export function InviteForm() {
  const { invite } = useInvitationMutations();
  const form = useForm<InviteValues>({
    resolver: zodResolver(inviteSchema),
    defaultValues: { email: "" },
  });

  const onSubmit = (values: InviteValues) =>
    invite.mutateAsync(values).then(
      (invitation) => {
        toast.success(`Invitation sent to ${invitation.email}`);
        form.reset();
      },
      (error: Error) => {
        toast.error(error.message);
      },
    );

  return (
    <form
      noValidate
      onSubmit={form.handleSubmit(onSubmit)}
      className="flex flex-col gap-3 sm:flex-row sm:items-start"
    >
      <Controller
        name="email"
        control={form.control}
        render={({ field, fieldState }) => (
          <Field data-invalid={fieldState.invalid} className="flex-1">
            <FieldLabel htmlFor="invite-email" className="sr-only">
              Email address
            </FieldLabel>
            <Input
              {...field}
              id="invite-email"
              type="email"
              autoComplete="off"
              placeholder="colleague@example.com"
              aria-invalid={fieldState.invalid}
            />
            {fieldState.invalid ? (
              <FieldError errors={[fieldState.error]} />
            ) : (
              <FieldDescription>
                They get an email with a link to verify their address and set a
                password. The link works for 7 days.
              </FieldDescription>
            )}
          </Field>
        )}
      />
      <Button type="submit" disabled={form.formState.isSubmitting}>
        <Send />
        {form.formState.isSubmitting ? "Sending…" : "Send invite"}
      </Button>
    </form>
  );
}
