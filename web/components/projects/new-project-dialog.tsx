"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { FolderPlus } from "lucide-react";
import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { Controller, useForm } from "react-hook-form";
import { toast } from "sonner";

import { useCreateProject } from "@/components/projects/use-projects";
import { useCatalog } from "@/components/requests/use-requests";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import {
  Field,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
} from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import type { VariableSpec } from "@/lib/catalog";
import {
  defaultVariableValues,
  formVariables,
  PROJECT_TYPE,
  type ProjectDetail,
  projectPath,
  toVariables,
  variableLabel,
  variablesSchema,
  type VariableValues,
} from "@/lib/projects";

/**
 * **New project**: a button that opens a dialog with the New project form, whose fields come from the `project` type's
 * manifest. Creating one takes the user to its page.
 * @returns the button and its dialog
 */
export function NewProjectDialog() {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const catalog = useCatalog();
  const projectType = catalog.data?.find((t) => t.id === PROJECT_TYPE);

  const onCreated = (project: ProjectDetail) => {
    setOpen(false);
    toast.success(`Creating project ${project.name}`);
    router.push(projectPath(project.id));
  };

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger render={<Button />}>
        <FolderPlus />
        New project
      </DialogTrigger>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>New project</DialogTitle>
          <DialogDescription>
            A DigitalOcean project holds related resources together, such as
            everything for one app. It&apos;s created as soon as you submit.{" "}
            {projectType?.costHint}
          </DialogDescription>
        </DialogHeader>
        {catalog.isError ? (
          <p role="alert" className="text-sm text-muted-foreground">
            Couldn&apos;t load the form: {catalog.error.message}
          </p>
        ) : catalog.isPending ? (
          <div className="flex flex-col gap-3">
            <Skeleton className="h-9 w-full" />
            <Skeleton className="h-9 w-full" />
          </div>
        ) : projectType ? (
          <NewProjectForm
            variables={projectType.variables}
            onCreated={onCreated}
          />
        ) : (
          <p role="alert" className="text-sm text-muted-foreground">
            Projects aren&apos;t in the catalog.
          </p>
        )}
      </DialogContent>
    </Dialog>
  );
}

/**
 * The New project form: a field for each of the `project` manifest's plain string variables, with its label,
 * description, default and rules, and a select where it has allowed values. Submitting calls `POST /api/projects`,
 * which proposes and confirms the project in one step; the API's errors show above the buttons.
 * @param variables - the `project` type's variables, from `GET /api/catalog`
 * @param onCreated - called with the new project
 * @returns the form
 */
export function NewProjectForm({
  variables,
  onCreated,
}: {
  variables: VariableSpec[];
  onCreated: (project: ProjectDetail) => void;
}) {
  const fields = useMemo(() => formVariables(variables), [variables]);
  const schema = useMemo(() => variablesSchema(fields), [fields]);
  const create = useCreateProject();
  const form = useForm<VariableValues>({
    resolver: zodResolver(schema),
    defaultValues: defaultVariableValues(fields),
  });

  const onSubmit = (values: VariableValues) =>
    create.mutateAsync(toVariables(values)).then(onCreated, () => {
      // Shown from create.error below.
    });

  return (
    <form
      noValidate
      onSubmit={form.handleSubmit(onSubmit)}
      className="flex flex-col gap-4"
    >
      <FieldGroup>
        {fields.map((variable) => (
          <Controller
            key={variable.name}
            name={variable.name}
            control={form.control}
            render={({ field, fieldState }) => {
              const id = `project-${variable.name}`;
              return (
                <Field data-invalid={fieldState.invalid}>
                  <FieldLabel htmlFor={id}>
                    {variableLabel(variable)}
                    {variable.required ? null : (
                      <span className="font-normal text-muted-foreground">
                        (optional)
                      </span>
                    )}
                  </FieldLabel>
                  {variable.allowedValues ? (
                    <Select
                      items={variable.allowedValues.map((v) => ({
                        value: String(v),
                        label: String(v),
                      }))}
                      value={field.value === "" ? null : field.value}
                      onValueChange={(value) => field.onChange(value ?? "")}
                    >
                      <SelectTrigger
                        id={id}
                        ref={field.ref}
                        onBlur={field.onBlur}
                        aria-invalid={fieldState.invalid}
                        className="w-full"
                      >
                        <SelectValue placeholder="Choose…" />
                      </SelectTrigger>
                      <SelectContent>
                        {variable.allowedValues.map((v) => (
                          <SelectItem key={String(v)} value={String(v)}>
                            {String(v)}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  ) : (
                    <Input
                      {...field}
                      id={id}
                      autoComplete="off"
                      maxLength={variable.maxLength}
                      aria-invalid={fieldState.invalid}
                    />
                  )}
                  {variable.description ? (
                    <FieldDescription>{variable.description}</FieldDescription>
                  ) : null}
                  {fieldState.invalid ? (
                    <FieldError errors={[fieldState.error]} />
                  ) : null}
                </Field>
              );
            }}
          />
        ))}
      </FieldGroup>
      {create.error ? (
        <p role="alert" className="text-sm text-destructive">
          {create.error.message}
        </p>
      ) : null}
      <DialogFooter>
        <DialogClose render={<Button type="button" variant="outline" />}>
          Cancel
        </DialogClose>
        <Button type="submit" disabled={form.formState.isSubmitting}>
          {form.formState.isSubmitting ? "Creating…" : "Create project"}
        </Button>
      </DialogFooter>
    </form>
  );
}
