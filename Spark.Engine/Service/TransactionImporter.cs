﻿/* 
 * Copyright (c) 2014, Furore (info@furore.com) and contributors
 * See the file CONTRIBUTORS for details.
 * 
 * This file is licensed under the BSD 3-Clause license
 * available at https://raw.github.com/furore-fhir/spark/master/LICENSE
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Support;

using Spark.Core;
using System.Net;

namespace Spark.Service
{
    public class TransactionImporter
    {
        Mapper<Key, Key> mapper;
        IList<Interaction> interactions;
        ILocalhost localhost;
        IGenerator generator;

        public TransactionImporter(ILocalhost localhost, IGenerator generator)
        {
            this.localhost = localhost;
            this.generator = generator;
            mapper = new Mapper<Key, Key>();
            interactions = new List<Interaction>();
        }

        public void Add(Interaction interaction)
        {
            interactions.Add(interaction);
        }

        public void AddRange(IEnumerable<Interaction> interactions)
        {
            foreach (Interaction i in interactions)
            {
                Add(i);
            }
        }

        public IList<Interaction> Localize()
        {
            LocalizeKeys();
            LocalizeReferences();
            return interactions;
        }

        void LocalizeKeys()
        {
            foreach (Interaction interaction in this.interactions)
            {
                LocalizeKey(interaction);
            }
        }

        void LocalizeReferences()
        {
            foreach (Interaction i in interactions)
            {
                LocalizeReferences(i.Resource);
            }
        }

        Key Remap(Key key)
        {
            Key newKey = generator.NextKey(key);
            return mapper.Remap(key, newKey);
        }

        Key RemapHistoryOnly(Key key)
        {
            Key newKey = generator.NextHistoryKey(key);
            return mapper.Remap(key, newKey);
        }

        void LocalizeKey(Interaction interaction)
        {
            Key key = interaction.Key.Clone();

            switch (localhost.GetKeyKind(key))
            {
                case KeyKind.Foreign:
                {
                    interaction.Key = Remap(key);
                    return;
                }
                case KeyKind.Temporary:
                {
                    interaction.Key = Remap(key);
                    return;
                }
                case KeyKind.Local:
                {
                    if (interaction.Method == Bundle.HTTPVerb.PUT)
                    {
                        interaction.Key = Remap(key);
                    }
                    else
                    {
                        interaction.Key = RemapHistoryOnly(key);
                    }
                    return;

                }
                case KeyKind.Internal:
                default:
                {
                    throw new SparkException("Client provided an key without a base: " + interaction.Key.ToString());
                }
            }
        }

        void LocalizeReferences(Resource resource)
        {
            Visitor action = (element, name) =>
            {
                if (element == null) return;

                if (element is ResourceReference)
                {
                    ResourceReference reference = (ResourceReference)element;
                    reference.Url = LocalizeReference(reference.Url);
                }
                else if (element is FhirUri)
                {
                    FhirUri uri = (FhirUri)element;
                    uri.Value = LocalizeReference(uri.Value);
                    //((FhirUri)element).Value = LocalizeReference(new Uri(((FhirUri)element).Value, UriKind.RelativeOrAbsolute)).ToString();
                }
                else if (element is Narrative)
                {
                    Narrative n = (Narrative)element;
                    n.Div = FixXhtmlDiv(n.Div);
                }

            };

            Type[] types = { typeof(ResourceReference), typeof(FhirUri), typeof(Narrative) };

            ResourceVisitor.VisitByType(resource, action, types);
        }

        Key LocalizeReference(Key original)
        {
            KeyKind triage = (localhost.GetKeyKind(original));
            if (triage == KeyKind.Foreign | triage == KeyKind.Temporary)
            {
                Key replacement = mapper.TryGet(original);
                if (replacement != null)
                {
                    return replacement;
                }
                else
                {
                    throw new SparkException(HttpStatusCode.Conflict, "This reference does not point to a resource in the server or the current transaction: {0}", original);
                }
            }
            else if (triage == KeyKind.Local)
            {
                return original.WithoutBase();
            }
            else
            {
                return original;
            }
        }

        Uri LocalizeReference(Uri uri)
        {
            if (uri == null) return null;
            
            if (localhost.IsBaseOf(uri))
            {
                Key key = localhost.UriToKey(uri);
                return LocalizeReference(key).ToUri();
            }
            else
            {
                return uri;
            }
        }

        String LocalizeReference(String uristring)
        {
            if (String.IsNullOrWhiteSpace(uristring)) return uristring;

            Uri uri = new Uri(uristring, UriKind.RelativeOrAbsolute);
            return LocalizeReference(uri).ToString();
        }

        string FixXhtmlDiv(string div)
        {
            XDocument xdoc = null;

            try
            {
                xdoc = XDocument.Parse(div);
            }
            catch
            {
                // illegal xml, don't bother, just return the argument
                // todo: should we really allow illegal xml ?
                return div;
            }

            var srcAttrs = xdoc.Descendants(Namespaces.XHtml + "img").Attributes("src");
            foreach (var srcAttr in srcAttrs)
            {
                srcAttr.Value = LocalizeReference(srcAttr.Value);
            }

            var hrefAttrs = xdoc.Descendants(Namespaces.XHtml + "a").Attributes("href");
            foreach (var hrefAttr in hrefAttrs)
            {
                hrefAttr.Value = LocalizeReference(hrefAttr.Value);
            }
            
            return xdoc.ToString();
        }

    }




}