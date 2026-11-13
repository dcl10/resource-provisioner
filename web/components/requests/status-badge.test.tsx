import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { StatusBadge } from "@/components/requests/status-badge";
import type { RequestStatus } from "@/lib/requests";

describe("StatusBadge", () => {
  it.each<[RequestStatus, string]>([
    ["AwaitingConfirmation", "Awaiting confirmation"],
    ["Queued", "Queued"],
    ["Applying", "Provisioning"],
    ["Succeeded", "Provisioned"],
    ["Failed", "Failed"],
    ["Destroying", "Destroying"],
    ["DestroyFailed", "Destroy failed"],
  ])("labels %s as %s", (status, label) => {
    render(<StatusBadge status={status} />);

    expect(screen.getByText(label)).toHaveAttribute("data-status", status);
  });

  it.each<RequestStatus>(["Queued", "Applying", "Destroying"])(
    "shows a spinner while %s",
    (status) => {
      const { container } = render(<StatusBadge status={status} />);

      expect(container.querySelector(".animate-spin")).not.toBeNull();
    },
  );

  it.each<RequestStatus>([
    "AwaitingConfirmation",
    "Succeeded",
    "Failed",
    "DestroyFailed",
  ])("shows no spinner once %s", (status) => {
    const { container } = render(<StatusBadge status={status} />);

    expect(container.querySelector(".animate-spin")).toBeNull();
  });

  it("marks a failure as destructive", () => {
    render(<StatusBadge status="Failed" />);

    expect(screen.getByText("Failed")).toHaveClass("bg-destructive/10");
  });

  it("marks a failed destroy as destructive, with a warning icon a failed apply doesn't have", () => {
    const { container, rerender } = render(
      <StatusBadge status="DestroyFailed" />,
    );

    expect(screen.getByText("Destroy failed")).toHaveClass("bg-destructive/10");
    expect(container.querySelector("svg")).not.toBeNull();

    rerender(<StatusBadge status="Failed" />);
    expect(container.querySelector("svg")).toBeNull();
  });
});
