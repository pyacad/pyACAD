"""Internal pure-python helpers (no AutoCAD API - unit-testable anywhere)."""


def flatten(items, is_leaf, is_group):
    """Depth-first flattening of a tree into its leaves.

    is_leaf is checked FIRST: a node that satisfies both predicates is yielded,
    not descended into. That ordering is load-bearing: a node type that is both a
    leaf and a group (it has children of its own) would otherwise be walked into and
    its children yielded instead of itself. Nodes matching neither are skipped.
    """
    for item in items:
        if is_leaf(item):
            yield item
        elif is_group(item):
            for child in flatten(item.Children, is_leaf, is_group):
                yield child


def flatten_with_path(items, is_folder, _folders=()):
    """Yields (folder_names, leaf) pairs, recursing ONLY into folder nodes.

    Anything that is not a folder is a leaf, even when it has Children of its own:
    such a node is reported as one item, not as its parts.
    """
    folders = list(_folders)
    for item in items:
        if is_folder(item):
            for pair in flatten_with_path(
                    item.Children, is_folder, folders + [item.DisplayName]):
                yield pair
        else:
            yield folders, item
