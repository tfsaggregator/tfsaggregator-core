using System;
using System.Collections;
using System.Collections.Generic;
using Aggregator.Core.Context;
using Aggregator.Core.Interfaces;
using Microsoft.TeamFoundation.WorkItemTracking.Client;

namespace Aggregator.Core.Facade
{
    internal class WorkItemLinkExposedCollectionWrapper : IWorkItemLinkExposedCollection
    {
        private readonly WorkItemLinkCollection workItemLinks;
        private readonly IRuntimeContext context;

        public WorkItemLinkExposedCollectionWrapper(WorkItemLinkCollection workItemLinks, IRuntimeContext context)
        {
            this.workItemLinks = workItemLinks;
            this.context = context;
        }

        public IEnumerator<IWorkItemLinkExposed> GetEnumerator()
        {
            foreach (WorkItemLink item in this.workItemLinks)
            {
                yield return new WorkItemLinkExposedWrapper(item, this.context);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException();
        }
    }
}